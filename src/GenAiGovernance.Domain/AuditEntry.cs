namespace GenAiGovernance.Domain;

/// <summary>
/// One row of the tamper-evident audit trail. <see cref="PriorHash"/> and
/// <see cref="EntryHash"/> are what make the trail a hash chain rather than
/// a plain log table: <see cref="Core.AuditTrailStore"/> computes
/// <c>EntryHash = SHA256(PriorHash + canonical(this entry's fields))</c>
/// before insert, so altering any historical row's stored fields breaks
/// that row's own hash and every hash after it. See the README's
/// "Verification" section for the live SQLite check that actually proves
/// this rather than just asserting it.
/// </summary>
public sealed record AuditEntry(
    long Id,
    DateTime TimestampUtc,
    string ActorId,
    string ActionDescription,
    string RiskLevel,
    double RiskScore,
    string GovernanceAction,
    string? Detail,
    string PriorHash,
    string EntryHash);
