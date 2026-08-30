namespace GenAiGovernance.Domain;

/// <summary>
/// What a mechanical governance check found. This is deliberately not a
/// bool -- "did it pass" throws away the information an auditor needs
/// (what was redacted, which rule fired, what the original text said
/// before it was rewritten). Every gate in this toolkit returns one of
/// these instead of a boolean, and <see cref="Core.GovernanceGate"/>
/// combines them into a single decision.
/// </summary>
/// <param name="Passed">
/// True only if the check needed no intervention at all. A redaction that
/// successfully removed PII still reports <c>false</c> here -- the content
/// changed, so a human auditing the trail should see that a rule fired,
/// even though the final output is safe to send.
/// </param>
/// <param name="RuleId">Stable identifier of the rule that produced this verdict, e.g. "PII-001".</param>
/// <param name="Detail">Human-readable explanation: what was found, what was done about it.</param>
/// <param name="TransformedText">
/// The text after this gate's transformation (redaction applied, disclaimer
/// appended). Equal to the input when <see cref="Passed"/> is true.
/// </param>
public sealed record GovernanceVerdict(bool Passed, string RuleId, string Detail, string TransformedText);

/// <summary>
/// The final action <see cref="Core.GovernanceGate"/> takes on a piece of
/// agent output, after combining every mechanical check's verdict.
/// </summary>
public enum GovernanceAction
{
    /// <summary>No rule fired. Output passes through unchanged.</summary>
    Allow,

    /// <summary>One or more rules rewrote the output (redaction, disclaimer). The rewritten text is still sent.</summary>
    AllowWithModification,

    /// <summary>A restricted-topic rule fired with no safe rewrite available. Nothing is sent to the customer.</summary>
    Block,

    /// <summary>The paired <see cref="RiskAssessment"/> put this action at or above the human-approval threshold.</summary>
    RequireHumanApproval,
}

/// <summary>
/// The complete, auditable outcome of running one piece of agent output
/// through <see cref="Core.GovernanceGate"/>: every individual verdict that
/// contributed, the final action, and the text that should actually be
/// released (or null, if <see cref="Action"/> is <c>Block</c> or
/// <c>RequireHumanApproval</c>).
/// </summary>
public sealed record GovernanceResult(
    GovernanceAction Action,
    IReadOnlyList<GovernanceVerdict> Verdicts,
    string? ReleasedText);
