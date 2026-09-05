using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 账本成员控制器（共享账本的成员与权限管理）
/// </summary>
[ApiController]
public class LedgerMembersController : BaseApiController
{
    private readonly ILedgerMemberService _ledgerMemberService;

    public LedgerMembersController(ILedgerMemberService ledgerMemberService)
    {
        _ledgerMemberService = ledgerMemberService;
    }

    /// <summary>
    /// 获取账本成员列表
    /// </summary>
    [HttpGet("~/api/ledgers/{ledgerId}/members")]
    public async Task<IActionResult> GetMembers(Guid ledgerId)
    {
        var userId = GetUserId();

        try
        {
            var members = await _ledgerMemberService.GetMembersAsync(ledgerId, userId);
            return Ok(members.Select(MapToResponse));
        }
        catch (ForbiddenAccessException ex)
        {
            // Forbid(string) 的参数是 authenticationScheme 而非消息，误用会导致 500；显式返回 403
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 邀请成员（Owner 专属，按手机号）
    /// </summary>
    [HttpPost("~/api/ledgers/{ledgerId}/members")]
    public async Task<IActionResult> Invite(Guid ledgerId, [FromBody] LedgerMemberRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new { message = "手机号不能为空" });
        }

        try
        {
            var member = await _ledgerMemberService.InviteAsync(
                ledgerId, userId, request.PhoneNumber, request.Role);

            // 返回完整同步 DTO（含时间戳与被邀请人手机号），客户端可直接合并缓存，无需等待下一次拉取
            return Ok(LedgerMemberSyncDto.FromEntity(member));
        }
        catch (ForbiddenAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 修改成员角色（Owner 专属）
    /// </summary>
    [HttpPut("~/api/ledgers/{ledgerId}/members/{memberId}/role")]
    public async Task<IActionResult> ChangeRole(Guid ledgerId, Guid memberId, [FromBody] LedgerMemberRequest request)
    {
        var userId = GetUserId();

        try
        {
            await _ledgerMemberService.ChangeRoleAsync(ledgerId, memberId, request.Role, userId);
            return NoContent();
        }
        catch (ForbiddenAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 移除成员（Owner 专属）
    /// </summary>
    [HttpDelete("~/api/ledgers/{ledgerId}/members/{memberId}")]
    public async Task<IActionResult> RemoveMember(Guid ledgerId, Guid memberId)
    {
        var userId = GetUserId();

        try
        {
            await _ledgerMemberService.RemoveMemberAsync(ledgerId, memberId, userId);
            return NoContent();
        }
        catch (ForbiddenAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 转让所有权（Owner 专属，原 Owner 降为 Editor）
    /// </summary>
    [HttpPost("~/api/ledgers/{ledgerId}/transfer-ownership")]
    public async Task<IActionResult> TransferOwnership(Guid ledgerId, [FromBody] LedgerTransferOwnershipRequest request)
    {
        var userId = GetUserId();

        try
        {
            await _ledgerMemberService.TransferOwnershipAsync(ledgerId, userId, request.ToUserId);
            return NoContent();
        }
        catch (ForbiddenAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 退出共享账本（Owner 须先转让所有权）
    /// </summary>
    [HttpPost("~/api/ledgers/{ledgerId}/exit")]
    public async Task<IActionResult> Exit(Guid ledgerId)
    {
        var userId = GetUserId();

        try
        {
            await _ledgerMemberService.ExitAsync(ledgerId, userId);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 获取当前用户的待处理邀请
    /// </summary>
    [HttpGet("~/api/invitations")]
    public async Task<IActionResult> GetInvitations()
    {
        var userId = GetUserId();
        var invitations = await _ledgerMemberService.GetPendingInvitationsAsync(userId);

        return Ok(invitations.Select(i => new
        {
            id = i.Id,
            ledgerId = i.LedgerId,
            ledgerName = i.Ledger?.Name,
            ledgerIcon = i.Ledger?.Icon,
            inviterPhoneNumber = i.Ledger?.User?.PhoneNumber,
            role = i.Role,
            createdAt = i.CreatedAt
        }));
    }

    /// <summary>
    /// 响应邀请（接受/拒绝）
    /// </summary>
    [HttpPost("~/api/invitations/{memberId}/respond")]
    public async Task<IActionResult> RespondInvitation(Guid memberId, [FromBody] LedgerInvitationRespondRequest request)
    {
        var userId = GetUserId();

        try
        {
            await _ledgerMemberService.RespondAsync(memberId, userId, request.Accept);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static object MapToResponse(LedgerMember member) => new
    {
        id = member.Id,
        ledgerId = member.LedgerId,
        userId = member.UserId,
        phoneNumber = member.User?.PhoneNumber,
        role = member.Role,
        status = member.Status
    };
}