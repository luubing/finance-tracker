using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Infrastructure.Data;

/// <summary>
/// 应用程序数据库上下文
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<PaymentChannel> PaymentChannels => Set<PaymentChannel>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();
    public DbSet<LedgerMember> LedgerMembers => Set<LedgerMember>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<PendingBill> PendingBills => Set<PendingBill>();
    public DbSet<UserDefault> UserDefaults => Set<UserDefault>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 用户配置
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PhoneNumber).IsUnique();
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(20);
            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // 账单配置
        modelBuilder.Entity<Bill>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.TransactionTime);
            entity.HasIndex(e => new { e.UserId, e.TransactionTime });
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Note).HasMaxLength(500);

            // 库中的 TransactionTime 约定为 UTC（见 Core 的 AppTimeZone 与 ADR 0005）。
            // EF Core 从 SQLite 读出的 DateTime 其 Kind 为 Unspecified，后续任何 ToUniversalTime/ToLocalTime
            // 都会把它误当本地时间二次换算；因此读出时统一标记为 Utc。
            // 写入时：Local 按应用时区转 UTC（与 AppTimeZone.ToUtc 的约定一致，而不是 ToUniversalTime() 的系统时区），
            // Unspecified 按 UTC 对待（调用方应已经过 BillService 的 AppTimeZone.ToUtc）。
            // 提供程序类型仍是 DateTime，列类型不变，因此无需新增迁移。
            entity.Property(e => e.TransactionTime).HasConversion(
                value => value.Kind == DateTimeKind.Local
                    ? AppTimeZone.ToUtc(value)
                    : DateTime.SpecifyKind(value, DateTimeKind.Utc),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

            entity.HasOne(e => e.User)
                .WithMany(u => u.Bills)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Category)
                .WithMany(c => c.Bills)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PaymentChannel)
                .WithMany(p => p.Bills)
                .HasForeignKey(e => e.PaymentChannelId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Ledger)
                .WithMany(l => l.Bills)
                .HasForeignKey(e => e.LedgerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // 分类配置
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Type });
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Icon).HasMaxLength(100);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Categories)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // 支付渠道配置
        modelBuilder.Entity<PaymentChannel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Icon).HasMaxLength(100);

            entity.HasOne(e => e.User)
                .WithMany(u => u.PaymentChannels)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // 账本配置
        modelBuilder.Entity<Ledger>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Icon).HasMaxLength(100);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Ledgers)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // 账本成员配置（共享账本）：(LedgerId, UserId) 唯一，拒绝/移除后重新邀请复用原记录
        modelBuilder.Entity<LedgerMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.LedgerId, e.UserId }).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Ledger)
                .WithMany(l => l.Members)
                .HasForeignKey(e => e.LedgerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(u => u.LedgerMemberships)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 待确认账单配置（通知栏/短信自动捕获的本地数据，不参与云同步，无外键关系）
        modelBuilder.Entity<PendingBill>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Source, e.TransactionTime });
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Channel).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Note).HasMaxLength(500);
        });

        // 预算配置
        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Year, e.Month });
            entity.Property(e => e.Amount).HasPrecision(18, 2);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Budgets)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Ledger)
                .WithMany()
                .HasForeignKey(e => e.LedgerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Category)
                .WithMany()
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // 用户默认值配置
        modelBuilder.Entity<UserDefault>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();

            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserDefault>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DefaultExpenseCategory)
                .WithMany()
                .HasForeignKey(e => e.DefaultExpenseCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DefaultIncomeCategory)
                .WithMany()
                .HasForeignKey(e => e.DefaultIncomeCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DefaultPaymentChannel)
                .WithMany()
                .HasForeignKey(e => e.DefaultPaymentChannelId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DefaultLedger)
                .WithMany()
                .HasForeignKey(e => e.DefaultLedgerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 同步作用域内由同步代码自己决定时间戳（见 SyncTimestampScope）：标记同步状态不算编辑，
        // 采用云端版本时要沿用云端的更新时间。其余所有保存照常盖章。
        if (!SyncTimestampScope.IsActive)
        {
            foreach (var entry in ChangeTracker.Entries<Core.Entities.BaseEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = DateTime.UtcNow;
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                        break;
                    case EntityState.Modified:
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                        break;
                }
            }
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 本应用的 DbContext 是长生命周期的（MAUI Blazor Hybrid 的 WebView 作用域 / Blazor Server 的电路）。
            // 一次保存失败后，失败的 Added/Modified/Deleted 实体若继续留在 ChangeTracker 中，
            // 之后每一次（哪怕完全合法的）保存都会连带重试这批脏数据并再次失败——
            // 表现为「保存失败: An error occurred while saving the entity changes」反复出现，重启应用才恢复。
            // 因此失败后丢弃本次未提交的变更（Unchanged 的已加载实体保持不动），异常原样抛给调用方。
            DiscardPendingChanges();
            throw;
        }
    }

    /// <summary>
    /// 丢弃所有待提交（Added/Modified/Deleted）的变更，使上下文恢复到可继续使用的状态。
    /// <para>
    /// 前提：同一个 DbContext 上没有别的流程同时持有尚未保存的变更。
    /// 本应用所有写入都是"改完立刻 SaveChanges"，保存失败时 ChangeTracker 里的待提交内容只可能属于这次保存；
    /// 若以后出现"先在上下文里攒一批改动、稍后一起保存"的流程，这里会连带丢掉它们，需要改成只丢弃失败批次涉及的实体
    /// （例如从 <see cref="DbUpdateException.Entries"/> 取）。
    /// </para>
    /// </summary>
    private void DiscardPendingChanges()
    {
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
