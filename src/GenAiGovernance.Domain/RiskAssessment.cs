namespace GenAiGovernance.Domain;

/// <summary>
/// The two levels the field guide's Section 02 defines around the >= 2.5
/// threshold on the mean of the four <see cref="RiskFactors"/>. Anything at
/// or above the threshold needs a human checkpoint before it executes, full
/// stop -- this library does not expose a way to promote a
/// <see cref="RequiresHumanApproval"/> action to auto-execute.
/// </summary>
public enum RiskLevel
{
    AutoExecutable,
    RequiresHumanApproval,
}

/// <summary>
/// The output of <c>AgenticRiskScorer.Score</c>: the raw factors, the
/// derived mean, the level the threshold maps it to, and a one-line
/// rationale naming the dimension(s) that drove the result -- so a human
/// reviewing an audit trail entry doesn't have to recompute the arithmetic
/// to understand why an action was gated.
/// </summary>
public sealed record RiskAssessment(RiskFactors Factors, double Score, RiskLevel Level, string Rationale);
