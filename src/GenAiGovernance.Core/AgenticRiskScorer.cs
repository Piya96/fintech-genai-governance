using GenAiGovernance.Domain;

namespace GenAiGovernance.Core;

/// <summary>
/// Scores an agentic action's <see cref="RiskFactors"/> into a
/// <see cref="RiskAssessment"/>, per the Tier 5 field guide's Section 02
/// (arXiv 2608.02311): mean of the four 1-3 dimensions, threshold 2.5.
///
/// The threshold is a straight arithmetic mean rather than a weighted one
/// on purpose -- the paper's own worked examples use an unweighted mean,
/// and a weighted version invites each institution to quietly tune the
/// weights until their preferred actions clear the bar, which defeats the
/// point of a mechanical, auditable score. If an institution genuinely
/// needs weights (e.g. treating Mutability as more load-bearing than
/// Autonomy), that's a documented policy decision to make explicitly at
/// the call site, not something this scorer should default to silently.
///
/// See <c>verification/risk_scorer_oracle.py</c> for a Python port of this
/// exact arithmetic, cross-checked against this class's logic across the
/// full 81-combination factor space (3^4) -- the kind of exhaustive check
/// that's cheap here because the input space is genuinely small.
/// </summary>
public static class AgenticRiskScorer
{
    public const double HumanApprovalThreshold = 2.5;

    public static RiskAssessment Score(RiskFactors factors)
    {
        double mean = (factors.Autonomy + factors.Reversibility + factors.BlastRadius + factors.Mutability) / 4.0;
        var level = mean >= HumanApprovalThreshold ? RiskLevel.RequiresHumanApproval : RiskLevel.AutoExecutable;

        var driving = new List<string>();
        if (factors.Autonomy == 3) driving.Add("Autonomy=3 (agent sets its own sub-goals)");
        if (factors.Reversibility == 3) driving.Add("Reversibility=3 (irreversible)");
        if (factors.BlastRadius == 3) driving.Add("BlastRadius=3 (affects many customers or a systemic process)");
        if (factors.Mutability == 3) driving.Add("Mutability=3 (writes propagate outside governance's visibility)");

        string rationale = level == RiskLevel.AutoExecutable
            ? $"Mean {mean:0.00} < {HumanApprovalThreshold} -- no dimension pattern requires a human checkpoint."
            : driving.Count > 0
                ? $"Mean {mean:0.00} >= {HumanApprovalThreshold}. Driven by: {string.Join("; ", driving)}."
                : $"Mean {mean:0.00} >= {HumanApprovalThreshold} on a broad mix of moderate (2) scores across all four dimensions -- no single dimension maxed out, but the combination still crosses the threshold.";

        return new RiskAssessment(factors, mean, level, rationale);
    }
}
