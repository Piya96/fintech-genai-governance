namespace GenAiGovernance.Domain;

/// <summary>
/// The four dimensions of the Agentic Risk Score, per the Tier 5 field guide's
/// Section 02 (arXiv 2608.02311's four-layer governance architecture names
/// this scoring step as the gate between the Policy layer and everything an
/// agent is actually allowed to execute at runtime).
///
/// Each dimension is scored 1-3 by the calling application (not by this
/// library -- deciding "is this action reversible" is a domain judgment,
/// not something a generic toolkit can infer). What this library owns is
/// combining the four scores the same way every time, and refusing to let
/// an agent runtime skip that combination.
/// </summary>
/// <param name="Autonomy">
/// 1 = human approves every step. 2 = human approves the plan, agent
/// executes multiple steps unsupervised. 3 = agent sets its own sub-goals
/// with no human checkpoint before acting.
/// </param>
/// <param name="Reversibility">
/// 1 = trivially undoable (a draft, a read-only query). 2 = undoable with
/// effort or delay (a refund, a support ticket). 3 = irreversible or
/// effectively so (funds transferred out of the institution, a filed
/// regulatory report, a closed account).
/// </param>
/// <param name="BlastRadius">
/// 1 = affects only the requesting customer's own record. 2 = affects a
/// bounded group (a household, a small batch of accounts). 3 = affects
/// many customers or a systemic process (a pricing model, a mass
/// communication, a risk model's parameters).
/// </param>
/// <param name="Mutability">
/// 1 = the action only reads or reports. 2 = the action writes but within
/// a single system of record. 3 = the action writes and that write
/// propagates to systems the agent's own governance layer cannot see or
/// undo (a downstream feed, a partner API, a regulator submission).
/// </param>
public sealed record RiskFactors(int Autonomy, int Reversibility, int BlastRadius, int Mutability)
{
    public RiskFactors
    {
        foreach (var (name, value) in new[]
                 {
                     (nameof(Autonomy), Autonomy), (nameof(Reversibility), Reversibility),
                     (nameof(BlastRadius), BlastRadius), (nameof(Mutability), Mutability),
                 })
        {
            if (value is < 1 or > 3)
                throw new ArgumentOutOfRangeException(name, value, $"{name} must be 1, 2, or 3.");
        }
    }
}
