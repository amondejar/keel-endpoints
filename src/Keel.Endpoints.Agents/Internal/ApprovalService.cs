using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Keel.Endpoints.Agents.Internal;

/// <summary>Where <c>MapKeelApprovals</c> put the approval pages.</summary>
internal sealed class ApprovalRoutes
{
    public string? Pattern { get; set; }
}

internal readonly record struct ApprovalSettings(int Approvals, string? Policy, bool RequesterMayApprove, bool RequesterOnly, TimeSpan Lifetime);

internal readonly record struct ApprovalOutcome(int Status, string Message, AgentApproval? Approval);

/// <summary>Creating approvals, the links to them, and the approvers' decisions with their rules.</summary>
internal sealed class ApprovalService(
    IAgentApprovalStore store,
    IAgentAuditSink audit,
    IAuthorizationService authorization,
    IHttpContextAccessor http,
    ApprovalRoutes routes,
    IOptions<KeelAgentsOptions> options,
    TimeProvider time)
{
    public const string Anonymous = "(anonymous)";

    public IAgentApprovalStore Store => store;

    public async ValueTask<AgentApproval> RequestAsync(string tool, string hash, string? user, string message, ApprovalSettings settings, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var approval = new AgentApproval
        {
            Id = RandomNumberGenerator.GetHexString(32, lowercase: true),
            Tool = tool,
            ArgumentsHash = hash,
            RequestedBy = user,
            Message = message,
            Created = now,
            Expires = now + settings.Lifetime,
            RequiredApprovals = settings.Approvals,
            Policy = settings.Policy,
            RequesterMayApprove = settings.RequesterMayApprove,
            RequesterOnly = settings.RequesterOnly,
            Status = AgentApprovalStatus.Pending,
        };
        await store.AddAsync(approval, cancellationToken);
        return approval;
    }

    public string Link(AgentApproval approval)
    {
        if (options.Value.ApprovalsUrl is { } configured) return configured.AbsoluteUri.TrimEnd('/') + "/" + approval.Id;
        var pattern = "/" + (routes.Pattern ?? "approvals").Trim('/');
        var request = http.HttpContext?.Request;
        var origin = request is null || !request.Host.HasValue ? string.Empty : $"{request.Scheme}://{request.Host}{request.PathBase}";
        return $"{origin}{pattern}/{approval.Id}";
    }

    /// <summary>Whether <paramref name="user"/> may approve or reject: the policy, four eyes, requester-only, identities for several approvers.</summary>
    public async ValueTask<(int Status, string? Error, string Approver)> CheckApproverAsync(AgentApproval approval, ClaimsPrincipal user)
    {
        var approver = Identity.Of(user);
        if (approval.Policy is { } policy)
        {
            if (approver is null) return (StatusCodes.Status401Unauthorized, "Sign in to approve this.", Anonymous);
            if (!(await authorization.AuthorizeAsync(user, policy)).Succeeded)
                return (StatusCodes.Status403Forbidden, "You are not allowed to approve this.", approver);
        }

        if (approval.RequesterOnly && approval.RequestedBy is { } requester && approver != requester)
            return (StatusCodes.Status403Forbidden, "Only the person whose agent asked can approve this.", approver ?? Anonymous);
        if (!approval.RequesterOnly && !approval.RequesterMayApprove && approval.RequestedBy is not null && approver == approval.RequestedBy)
            return (StatusCodes.Status403Forbidden, "You cannot approve your own agent's request: someone else must.", approver);
        if (approval.RequiredApprovals > 1 && approver is null)
            return (StatusCodes.Status401Unauthorized, "Sign in to approve this: it needs several different approvers.", Anonymous);

        return (StatusCodes.Status200OK, null, approver ?? Anonymous);
    }

    public async ValueTask<ApprovalOutcome> DecideAsync(string id, bool approve, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var approval = await store.GetAsync(id, cancellationToken);
            if (approval is null) return new(StatusCodes.Status404NotFound, "There is no such approval.", null);
            if (approval.Expires <= time.GetUtcNow()) return new(StatusCodes.Status410Gone, "This approval has expired.", approval);
            if (approval.Status != AgentApprovalStatus.Pending) return new(StatusCodes.Status409Conflict, $"This was already {Describe(approval.Status)}.", approval);

            var (status, error, approver) = await CheckApproverAsync(approval, user);
            if (error is not null) return new(status, error, approval);

            AgentApproval updated;
            if (approve)
            {
                if (approver != Anonymous && approval.ApprovedBy.Contains(approver, StringComparer.Ordinal))
                    return new(StatusCodes.Status409Conflict, "You already approved this: someone else must too.", approval);
                IReadOnlyList<string> approvedBy = [.. approval.ApprovedBy, approver];
                updated = approval with
                {
                    ApprovedBy = approvedBy,
                    Status = approvedBy.Count >= approval.RequiredApprovals ? AgentApprovalStatus.Approved : AgentApprovalStatus.Pending,
                    Version = approval.Version + 1,
                };
            }
            else
            {
                updated = approval with { Status = AgentApprovalStatus.Rejected, RejectedBy = approver, Version = approval.Version + 1 };
            }

            if (!await store.TryReplaceAsync(approval, updated, cancellationToken)) continue;

            await audit.WriteAsync(new AgentAuditEntry(
                time.GetUtcNow(), approval.Tool, approver == Anonymous ? null : approver,
                approve ? AgentDecision.ApprovalGranted : AgentDecision.ApprovalRejected,
                approval.Message, approval.ArgumentsHash, null), cancellationToken);

            var texts = options.Value.ApprovalTexts;
            var message = updated.Status switch
            {
                AgentApprovalStatus.Approved => texts.AllowedResult,
                AgentApprovalStatus.Rejected => texts.DeniedResult,
                _ => string.Format(System.Globalization.CultureInfo.InvariantCulture, texts.RecordedResult, updated.ApprovedBy.Count, updated.RequiredApprovals),
            };
            return new(StatusCodes.Status200OK, message, updated);
        }

        return new(StatusCodes.Status409Conflict, "Someone else changed this approval at the same time. Reload and try again.", null);
    }

    public static string Describe(AgentApprovalStatus status) => status switch
    {
        AgentApprovalStatus.Approved => "approved",
        AgentApprovalStatus.Rejected => "rejected",
        _ => "pending",
    };
}

internal static class Identity
{
    /// <summary>The caller's identifier (<c>NameIdentifier</c>, else <c>Name</c>), or <see langword="null"/> when anonymous.</summary>
    public static string? Of(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity.Name
            : null;
}
