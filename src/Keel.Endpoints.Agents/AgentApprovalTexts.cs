namespace Keel.Endpoints.Agents;

/// <summary>
/// The texts of the approval page and of its answers, so approvers read them in their language
/// (<see cref="KeelAgentsOptions.ApprovalTexts"/>). English by default; <see cref="Spanish"/> is built in.
/// </summary>
public sealed class AgentApprovalTexts
{
    /// <summary>The page title.</summary>
    public string Title { get; init; } = "An AI agent asks for permission";

    /// <summary>Above the request: what the approver decides.</summary>
    public string Question { get; init; } = "Allow the agent to do this?";

    /// <summary>The button that lets the agent do it.</summary>
    public string Allow { get; init; } = "Allow";

    /// <summary>The button that stops it.</summary>
    public string Deny { get; init; } = "Deny";

    /// <summary>Label: the tool.</summary>
    public string Tool { get; init; } = "Tool";

    /// <summary>Label: who asked.</summary>
    public string AskedBy { get; init; } = "Asked by";

    /// <summary>When nobody signed in asked.</summary>
    public string Anonymous { get; init; } = "an anonymous caller";

    /// <summary>Label: the status.</summary>
    public string Status { get; init; } = "Status";

    /// <summary>Label: the approvals so far.</summary>
    public string Approvals { get; init; } = "Approvals";

    /// <summary>Label: when it expires.</summary>
    public string Expires { get; init; } = "Expires";

    /// <summary>Status: waiting.</summary>
    public string Pending { get; init; } = "waiting for a decision";

    /// <summary>Status: allowed.</summary>
    public string Allowed { get; init; } = "allowed";

    /// <summary>Status: denied.</summary>
    public string Denied { get; init; } = "denied";

    /// <summary>Status: expired.</summary>
    public string Expired { get; init; } = "expired";

    /// <summary><c>{0} of {1}</c>.</summary>
    public string Count { get; init; } = "{0} of {1}";

    /// <summary>After allowing, with enough approvals.</summary>
    public string AllowedResult { get; init; } = "Allowed. The agent's next call will do it.";

    /// <summary>After denying.</summary>
    public string DeniedResult { get; init; } = "Denied. It will not be done.";

    /// <summary>After allowing, when more approvals are needed: <c>{0}</c> so far, <c>{1}</c> needed.</summary>
    public string RecordedResult { get; init; } = "Your approval is recorded: {0} of {1}.";

    /// <summary>The link does not match an approval.</summary>
    public string NotFound { get; init; } = "There is no such approval. The link may be wrong, or the server was restarted.";

    /// <summary>The approver may not decide.</summary>
    public string NotAllowed { get; init; } = "You are not allowed to decide this.";

    /// <summary>Texts in Spanish.</summary>
    public static AgentApprovalTexts Spanish { get; } = new()
    {
        Title = "Un agente de IA pide permiso",
        Question = "¿Permitir que el agente haga esto?",
        Allow = "Permitir",
        Deny = "Denegar",
        Tool = "Herramienta",
        AskedBy = "Lo pide",
        Anonymous = "un usuario anónimo",
        Status = "Estado",
        Approvals = "Aprobaciones",
        Expires = "Caduca",
        Pending = "pendiente de decisión",
        Allowed = "permitido",
        Denied = "denegado",
        Expired = "caducado",
        Count = "{0} de {1}",
        AllowedResult = "Permitido. La próxima llamada del agente lo hará.",
        DeniedResult = "Denegado. No se hará.",
        RecordedResult = "Tu aprobación queda registrada: {0} de {1}.",
        NotFound = "No existe esa aprobación. El enlace puede estar mal, o el servidor se reinició.",
        NotAllowed = "No puedes decidir esto.",
    };
}
