namespace Keel.Endpoints.Agents.Internal;

/// <summary>The default store: one instance, lost on restart. Expired approvals are dropped as new ones arrive.</summary>
internal sealed class InMemoryApprovalStore(TimeProvider time) : IAgentApprovalStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AgentApproval> _approvals = new(StringComparer.Ordinal);

    public ValueTask<AgentApproval?> GetAsync(string id, CancellationToken cancellationToken)
    {
        lock (_gate) return ValueTask.FromResult(_approvals.GetValueOrDefault(id));
    }

    public ValueTask<AgentApproval?> FindOpenAsync(string tool, string argumentsHash, string? requestedBy, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_approvals.Values
                .Where(a => !a.Consumed && a.Tool == tool && a.ArgumentsHash == argumentsHash && a.RequestedBy == requestedBy)
                .OrderByDescending(a => a.Created)
                .FirstOrDefault());
        }
    }

    public ValueTask AddAsync(AgentApproval approval, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            foreach (var expired in _approvals.Values.Where(a => a.Expires < now).Select(a => a.Id).ToList()) _approvals.Remove(expired);
            _approvals.Add(approval.Id, approval);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryReplaceAsync(AgentApproval current, AgentApproval updated, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_approvals.TryGetValue(current.Id, out var stored) || stored.Version != current.Version) return ValueTask.FromResult(false);
            _approvals[current.Id] = updated;
            return ValueTask.FromResult(true);
        }
    }
}
