using Microsoft.Extensions.Logging;

namespace Keel.Endpoints.Agents.Internal;

internal sealed partial class LoggingAuditSink(ILoggerFactory loggers) : IAgentAuditSink
{
    private readonly ILogger _logger = loggers.CreateLogger("Keel.Endpoints.Agents");

    public ValueTask WriteAsync(AgentAuditEntry entry, CancellationToken cancellationToken)
    {
        Log(_logger, entry.Tool, entry.User ?? "(anonymous)", entry.Decision, entry.Succeeded, entry.ArgumentsHash, entry.Summary);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent tool {Tool} by {User}: {Decision} (succeeded: {Succeeded}, arguments {ArgumentsHash}) {Summary}")]
    private static partial void Log(ILogger logger, string tool, string user, AgentDecision decision, bool? succeeded, string argumentsHash, string? summary);
}
