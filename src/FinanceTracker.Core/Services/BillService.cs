using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 账单服务实现
/// </summary>
public class BillService : IBillService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<BillService> _logger;

    public BillService(IApplicationDbContext context, ILogger<BillService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task<List<Bill>> GetBillsAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        Guid? ledgerId = null,
        BillType? type = null,
        int page = 1,
        int pageSize = 20)
    {
        return GetBillsAsync(
            userId,
            new BillFilter
            {
                StartDate = startDate,
                EndDate = endDate,
                CategoryIds = categoryId.HasValue ? [categoryId.Value] : null,
                PaymentChannelId = paymentChannelId,
                LedgerId = ledgerId,
                Type = type
            },
            page,
            pageSize);
    }

    public async Task<List<Bill>> GetBillsAsync(Guid userId, BillFilter filter, int page = 1, int pageSize = 20)
    {
        var query = await BuildBillQueryAsync(userId, filter);

        // 交易时间相同的账单（例如导入、同一分钟里连记几笔）要有确定的先后，否则分页时同一笔可能重复出现或被漏掉
        return await query
            .OrderByDescending(b => b.TransactionTime)
            .ThenByDescending(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
            .Include(b => b.Ledger)
            .ToListAsync();
    }

    public async Task<int> GetBillCountAsync(Guid userId, BillFilter filter)
    {
        var query = await BuildBillQueryAsync(userId, filter);

        return await query.CountAsync();
    }

    public async Task<BillSummary> GetBillSummaryAsync(Guid userId, BillFilter filter)
    {
        var query = await BuildBillQueryAsync(userId, filter);

        // 在库里按类型分组求和（不把全部账单读进内存）：账单再多，汇总也只读回至多两行
        var groups = await query
            .GroupBy(b => b.Type)
            .Select(g => new { Type = g.Key, Amount = g.Sum(b => b.Amount), Count = g.Count() })
            .ToListAsync();

        var expense = groups.FirstOrDefault(g => g.Type == BillType.Expense);
        var income = groups.FirstOrDefault(g => g.Type == BillType.Income);

        return new BillSummary(
            expense?.Amount ?? 0m,
            income?.Amount ?? 0m,
            groups.Sum(g => g.Count));
    }

    /// <summary>
    /// 账单查询的唯一构造点：列表、总数、汇总都从这里取「符合条件的账单」，三者口径不会各自漂移。
    /// 只负责条件（用户 / 软删除 / 账本可见性 / 未归属账本 / 时间 / 分类 / 支付渠道 / 类型 / 关键字），不含排序、分页与导航属性。
    /// </summary>
    private async Task<IQueryable<Bill>> BuildBillQueryAsync(Guid userId, BillFilter filter)
    {
        var query = _context.Bills
            .Where(b => b.UserId == userId && !b.IsDeleted);

        if (filter.LedgerId.HasValue)
        {
            var ledgerId = filter.LedgerId.Value;

            // 共享账本可见性：当筛选的账本为当前用户的共享账本时，
            // 账本内全部成员的账单对该用户可见（只读展示，仅排除已删除）
            var sharedLedgerIds = await GetSharedLedgerIdsAsync(userId);

            if (sharedLedgerIds.Contains(ledgerId))
            {
                query = _context.Bills
                    .Where(b => b.LedgerId == ledgerId && !b.IsDeleted);
            }
            else
            {
                query = query.Where(b => b.LedgerId == ledgerId);
            }
        }

        // 「未归属账本」：只看历史上没有账本的账单。放在账本分支之后、按取交集处理：
        // 与 LedgerId 同时给出时必然为空（共享账本分支会换掉整个起点，所以不能放在它前面）
        if (filter.UnassignedLedger)
        {
            query = query.Where(b => b.LedgerId == null);
        }

        // 筛选日期是应用时区的墙上时间，库里的 TransactionTime 是 UTC：先换算再比较
        if (filter.StartDate.HasValue)
        {
            var startUtc = AppTimeZone.ToUtc(filter.StartDate.Value);
            query = query.Where(b => b.TransactionTime >= startUtc);
        }

        if (filter.EndDate.HasValue)
        {
            var endUtc = AppTimeZone.ToUtc(filter.EndDate.Value);
            query = query.Where(b => b.TransactionTime <= endUtc);
        }

        // 分类可多选：属于其中任意一个即可（IN 查询）；用 List 而不是只读接口，EF 才会把 Contains 翻成 IN
        if (filter.CategoryIds is { Count: > 0 } categoryIds)
        {
            var ids = categoryIds.Distinct().ToList();
            query = query.Where(b => ids.Contains(b.CategoryId));
        }

        if (filter.PaymentChannelId.HasValue)
        {
            var paymentChannelId = filter.PaymentChannelId.Value;
            query = query.Where(b => b.PaymentChannelId == paymentChannelId);
        }

        if (filter.Type.HasValue)
        {
            var type = filter.Type.Value;
            query = query.Where(b => b.Type == type);
        }

        // 关键字搜索：备注或分类名称包含（不区分大小写），关键字整体是数字时再加上「金额等于该数值」。
        // 与上面的条件取交集；放在最后，对共享账本的起点替换也同样生效。
        // 文本匹配用 ToLower + Contains（SQLite 里是 lower + instr）：关键字里的 % _ 当字面量，不会被当成 LIKE 通配符。
        // 注意 SQLite 内置的 lower() 只折叠 ASCII 字母——中文没有大小写，英文备注 / 分类名（Costco、Gym）不受影响。
        if (BillKeyword.Parse(filter.Keyword) is { } keyword)
        {
            var text = keyword.Text;

            if (keyword.Amount is { } amount)
            {
                // 金额用「>= 且 <=」表达相等，不写 ==：SQLite 把 decimal 存成文本，== 是文本比较，
                // 只有库里的文本恰好是 EF 写入时的规整格式（12.50 写成 "12.5"）才碰巧相等；
                // 大于 / 小于比较会走 EF 注册的 ef_compare（按数值比），12.5 与 "12.50" 也相等，不依赖库里的文本格式。
                // PostgreSQL 是 numeric，两种写法等价
                query = query.Where(b =>
                    b.Note!.ToLower().Contains(text)
                    || b.Category.Name.ToLower().Contains(text)
                    || (b.Amount >= amount && b.Amount <= amount));
            }
            else
            {
                query = query.Where(b =>
                    b.Note!.ToLower().Contains(text)
                    || b.Category.Name.ToLower().Contains(text));
            }
        }

        return query;
    }

    public async Task<Bill?> GetBillByIdAsync(Guid billId)
    {
        return await _context.Bills
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
            .Include(b => b.Ledger)
            .FirstOrDefaultAsync(b => b.Id == billId);
    }

    public async Task<Bill> CreateBillAsync(Bill bill)
    {
        try
        {
            // 先校验外键引用，避免把「FOREIGN KEY constraint failed」这类用户看不懂的数据库错误抛到界面
            await EnsureReferencesExistAsync(bill);

            bill.Id = Guid.NewGuid();
            bill.SyncStatus = SyncStatus.Pending;
            // 数据来源（Source）由调用方决定（手动录入/导入/短信识别/通知栏/语音识别），
            // 不在此处强制覆盖，否则语音等非手动来源的账单会被错误标记为手动录入

            // 入库统一为 UTC：调用方传入的 Local/Unspecified 都视为应用时区的墙上时间（见 AppTimeZone）
            bill.TransactionTime = AppTimeZone.ToUtc(bill.TransactionTime);

            _context.Bills.Add(bill);
            await _context.SaveChangesAsync();

            // 重新加载包含导航属性的数据
            return await GetBillByIdAsync(bill.Id) ?? bill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建账单失败，用户: {UserId}, 金额: {Amount}, 分类: {CategoryId}",
                bill.UserId, bill.Amount, bill.CategoryId);
            throw;
        }
    }

    public async Task<Bill> UpdateBillAsync(Bill bill)
    {
        try
        {
            var existingBill = await _context.Bills
                .FirstOrDefaultAsync(b => b.Id == bill.Id && b.UserId == bill.UserId);

            if (existingBill == null)
            {
                throw new ArgumentException("账单不存在");
            }

            // 只读成员不能修改共享账本里的账单——包括把它挪去自己的账本（那同样是在改只读账本里的账单）
            await EnsureNotReadOnlyMemberAsync(existingBill.LedgerId, bill.UserId, "修改");

            // 必须在修改已跟踪的 existingBill 之前校验：校验失败时不能把改了一半的实体留在 ChangeTracker 里，
            // 否则会连带污染之后的每一次保存
            await EnsureReferencesExistAsync(bill);

            existingBill.Amount = bill.Amount;
            existingBill.Type = bill.Type;
            existingBill.CategoryId = bill.CategoryId;
            existingBill.PaymentChannelId = bill.PaymentChannelId;
            existingBill.LedgerId = bill.LedgerId;

            // 入库统一为 UTC：调用方传入的 Local/Unspecified 都视为应用时区的墙上时间（见 AppTimeZone）
            existingBill.TransactionTime = AppTimeZone.ToUtc(bill.TransactionTime);

            existingBill.Note = bill.Note;
            existingBill.SyncStatus = SyncStatus.Pending;

            await _context.SaveChangesAsync();

            // 重新加载包含导航属性的数据
            return await GetBillByIdAsync(existingBill.Id) ?? existingBill;
        }
        catch (Exception ex) when (ex is not ForbiddenAccessException)
        {
            // 权限拒绝是预期内的业务结果（调用方转成 403 / 提示），不记成错误日志
            _logger.LogError(ex, "更新账单失败，账单Id: {BillId}, 用户: {UserId}", bill.Id, bill.UserId);
            throw;
        }
    }

    public async Task<bool> DeleteBillAsync(Guid billId, Guid userId)
    {
        try
        {
            var bill = await _context.Bills
                .FirstOrDefaultAsync(b => b.Id == billId && b.UserId == userId);

            if (bill == null)
            {
                return false;
            }

            // 只读成员不能删除共享账本里的账单：删除要随同步推到云端，云端对只读成员的推送一律拒绝，
            // 放行只会留下一笔「本地已删、云端还在」永远对不上的账单
            await EnsureNotReadOnlyMemberAsync(bill.LedgerId, userId, "删除");

            bill.IsDeleted = true;
            bill.SyncStatus = SyncStatus.Pending;

            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex) when (ex is not ForbiddenAccessException)
        {
            _logger.LogError(ex, "删除账单失败，账单Id: {BillId}, 用户: {UserId}", billId, userId);
            throw;
        }
    }

    public async Task<int> AssignBillsToLedgerAsync(List<Guid> billIds, Guid userId, Guid ledgerId)
    {
        try
        {
            if (billIds == null || billIds.Count == 0)
            {
                return 0;
            }

            // 账本必填：只能归属到某个账本，不再支持"移出账本"（那会产生新的未归属账单）
            if (ledgerId == Guid.Empty)
            {
                throw new ArgumentException("请选择账本");
            }

            // 目标账本必须属于当前用户且未删除
            var ledgerExists = await _context.Ledgers
                .AnyAsync(l => l.Id == ledgerId && l.UserId == userId && !l.IsDeleted);

            if (!ledgerExists)
            {
                throw new ArgumentException("账本不存在");
            }

            var bills = await _context.Bills
                .Where(b => billIds.Contains(b.Id) && b.UserId == userId && !b.IsDeleted)
                .ToListAsync();

            // 只读成员不能把共享账本里的账单改归属到别的账本（那是在修改只读账本里的账单）。
            // 整批先查后改：只要有一笔在只读账本里就整批拒绝，不会出现「一半改了一半没改」
            var sourceLedgerIds = bills.Where(b => b.LedgerId.HasValue).Select(b => b.LedgerId!.Value).Distinct().ToList();
            if ((await LedgerMemberQueries.GetReadOnlyLedgerIdsAsync(_context, userId, sourceLedgerIds)).Count > 0)
            {
                throw new ForbiddenAccessException(ReadOnlyMemberMessage("修改"));
            }

            foreach (var bill in bills)
            {
                bill.LedgerId = ledgerId;
                bill.SyncStatus = SyncStatus.Pending;
            }

            await _context.SaveChangesAsync();

            return bills.Count;
        }
        catch (Exception ex) when (ex is not ForbiddenAccessException)
        {
            _logger.LogError(ex, "批量分配账单到账本失败，用户: {UserId}, 账本Id: {LedgerId}, 账单数量: {Count}",
                userId, ledgerId, billIds?.Count ?? 0);
            throw;
        }
    }

    /// <summary>只读成员被拒绝时的提示（与 <c>LedgerMemberService.EnsureCanWriteAsync</c> 的「只读成员不能在共享账本中记账」同一说法）</summary>
    private static string ReadOnlyMemberMessage(string action) => $"只读成员不能{action}共享账本中的账单";

    /// <summary>
    /// 修改 / 删除 / 改归属账单之前的权限校验（ADR 0004：只读成员不能修改任何账单）：
    /// 账单<b>现在</b>所在的账本里，用户是已生效的只读成员（Viewer）就拒绝，抛 <see cref="ForbiddenAccessException"/>
    /// （已认证但无权操作，对应 403；绝不能抛 <see cref="UnauthorizedAccessException"/>，否则客户端会误判为登录过期而登出）。
    /// <para>
    /// 只拦「现役只读成员」这一种身份：历史上没有账本的账单、账本已删除 / 本地读不到、被移除的成员（ADR 0004：本地账单副本保留，处置权仍在自己手里）
    /// 都照常放行——否则这些账单就成了谁也动不了的死数据。账本是否可写由数据库里的成员关系判断，不信调用方传来的任何标记。
    /// </para>
    /// </summary>
    /// <param name="ledgerId">账单修改前所在的账本；没有账本（历史账单）直接放行</param>
    /// <param name="userId">当前用户</param>
    /// <param name="action">动作，用在提示里：修改 / 删除</param>
    private async Task EnsureNotReadOnlyMemberAsync(Guid? ledgerId, Guid userId, string action)
    {
        if (ledgerId is not { } id || id == Guid.Empty)
        {
            return;
        }

        if ((await LedgerMemberQueries.GetReadOnlyLedgerIdsAsync(_context, userId, [id])).Count > 0)
        {
            throw new ForbiddenAccessException(ReadOnlyMemberMessage(action));
        }
    }

    public Task<int> GetBillCountAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        Guid? ledgerId = null,
        BillType? type = null)
    {
        return GetBillCountAsync(
            userId,
            new BillFilter
            {
                StartDate = startDate,
                EndDate = endDate,
                CategoryIds = categoryId.HasValue ? [categoryId.Value] : null,
                PaymentChannelId = paymentChannelId,
                LedgerId = ledgerId,
                Type = type
            });
    }

    /// <summary>
    /// 保存前校验账单引用的用户/分类/支付渠道/账本是否存在，
    /// 把数据库外键错误提前转换为明确、可操作的业务提示。
    /// 账本必填：新建与编辑都不允许 LedgerId 为空（历史上未归属账本的账单，编辑时必须补上账本才能保存）。
    /// 使用 IgnoreQueryFilters：只校验外键行是否存在（与数据库约束语义一致），
    /// 因此编辑引用了「已软删除分类/渠道/账本」的历史账单不会被误拦截。
    /// </summary>
    /// <param name="bill">待保存的账单</param>
    /// <exception cref="ArgumentException">未选择账本，或引用的实体不存在</exception>
    private async Task EnsureReferencesExistAsync(Bill bill)
    {
        if (!await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == bill.UserId))
        {
            throw new ArgumentException("当前登录用户不存在，请退出并重新登录后再试");
        }

        if (!await _context.Categories.IgnoreQueryFilters().AnyAsync(c => c.Id == bill.CategoryId))
        {
            throw new ArgumentException("分类不存在，请重新选择分类");
        }

        if (!await _context.PaymentChannels.IgnoreQueryFilters().AnyAsync(c => c.Id == bill.PaymentChannelId))
        {
            throw new ArgumentException("支付渠道不存在，请重新选择支付渠道");
        }

        if (!bill.LedgerId.HasValue || bill.LedgerId.Value == Guid.Empty)
        {
            throw new ArgumentException("请选择账本");
        }

        if (!await _context.Ledgers.IgnoreQueryFilters().AnyAsync(l => l.Id == bill.LedgerId.Value))
        {
            throw new ArgumentException("账本不存在或尚未同步，请重新选择账本");
        }
    }

    /// <summary>
    /// 获取当前用户作为生效成员（非 Owner）参与的共享账本ID列表（含 Owner 角色行：
    /// Owner 也是成员，但自有账本走 UserId 过滤即可，此处含 Owner 无副作用）
    /// </summary>
    private async Task<List<Guid>> GetSharedLedgerIdsAsync(Guid userId)
    {
        return await _context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Active && !m.IsDeleted)
            .Select(m => m.LedgerId)
            .ToListAsync();
    }
}
