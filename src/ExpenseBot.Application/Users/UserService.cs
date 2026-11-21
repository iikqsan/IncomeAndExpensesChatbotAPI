using ExpenseBot.Application.Abstractions;
using ExpenseBot.Application.Domain;
using ExpenseBot.Application.Options;
using ExpenseBot.Application.Updates;
using Microsoft.Extensions.Options;

namespace ExpenseBot.Application.Users;

public enum DecisionOutcome
{
    Done,
    UserNotFound,
    AlreadyDecided,
}

public sealed record DecisionResult(DecisionOutcome Outcome, BotUser? User = null);

/// <summary>Registration, approval and permission checks. Access is decided by <c>users.status</c>.</summary>
public sealed class UserService(IUserRepository users, IOptions<BotOptions> options)
{
    private readonly BotOptions _options = options.Value;

    public long AdminUserId => _options.AdminUserId;

    public bool IsConfiguredAdmin(long userId) => userId == _options.AdminUserId && userId != 0;

    /// <summary>
    /// Loads the user (creating a <c>pending</c> member on first contact) and refreshes username/displayName.
    /// The configured admin is always kept as an active admin.
    /// </summary>
    public async Task<BotUser> EnsureUserAsync(BotSender sender, CancellationToken ct)
    {
        var isAdmin = IsConfiguredAdmin(sender.Id);
        var user = await users.UpsertProfileAsync(sender.Id, sender.Username, sender.DisplayName, new BotUser
        {
            Id = sender.Id,
            Role = isAdmin ? UserRole.Admin : UserRole.Member,
            Status = isAdmin ? UserStatus.Active : UserStatus.Pending,
            Timezone = _options.DefaultTimezone,
        }, ct);

        if (isAdmin && (user.Role != UserRole.Admin || user.Status != UserStatus.Active))
            user = await users.SetAccessAsync(sender.Id, UserRole.Admin, UserStatus.Active, ct);

        return user;
    }

    /// <summary>Approve (<c>active</c>) or reject (<c>blocked</c>) a pending user.</summary>
    public async Task<DecisionResult> DecideAsync(long targetUserId, bool approve, CancellationToken ct)
    {
        var target = await users.GetAsync(targetUserId, ct);
        if (target is null)
            return new DecisionResult(DecisionOutcome.UserNotFound);
        if (target.Status != UserStatus.Pending)
            return new DecisionResult(DecisionOutcome.AlreadyDecided, target);

        target.Status = approve ? UserStatus.Active : UserStatus.Blocked;
        await users.SetStatusAsync(targetUserId, target.Status, ct);
        return new DecisionResult(DecisionOutcome.Done, target);
    }

    /// <summary>Blocks a user. The configured admin cannot be blocked.</summary>
    public async Task<DecisionResult> BlockAsync(long targetUserId, CancellationToken ct)
    {
        if (IsConfiguredAdmin(targetUserId))
            return new DecisionResult(DecisionOutcome.AlreadyDecided);

        var target = await users.GetAsync(targetUserId, ct);
        if (target is null)
            return new DecisionResult(DecisionOutcome.UserNotFound);
        if (target.Status == UserStatus.Blocked)
            return new DecisionResult(DecisionOutcome.AlreadyDecided, target);

        target.Status = UserStatus.Blocked;
        await users.SetStatusAsync(targetUserId, UserStatus.Blocked, ct);
        return new DecisionResult(DecisionOutcome.Done, target);
    }

    /// <summary>Re-activates a blocked user. Pending users go through the approval buttons instead.</summary>
    public async Task<DecisionResult> UnblockAsync(long targetUserId, CancellationToken ct)
    {
        var target = await users.GetAsync(targetUserId, ct);
        if (target is null)
            return new DecisionResult(DecisionOutcome.UserNotFound);
        if (target.Status != UserStatus.Blocked)
            return new DecisionResult(DecisionOutcome.AlreadyDecided, target);

        target.Status = UserStatus.Active;
        await users.SetStatusAsync(targetUserId, UserStatus.Active, ct);
        return new DecisionResult(DecisionOutcome.Done, target);
    }

    public Task<IReadOnlyList<BotUser>> ListAsync(CancellationToken ct) => users.ListAllAsync(ct);
}
