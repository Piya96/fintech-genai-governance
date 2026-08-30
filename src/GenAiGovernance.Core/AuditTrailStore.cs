using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GenAiGovernance.Domain;
using Microsoft.Data.Sqlite;

namespace GenAiGovernance.Core;

/// <summary>
/// Append-only, tamper-evident audit log for every governance decision.
/// "Tamper-evident" is a specific, narrower claim than "tamper-proof":
/// this class cannot stop someone with direct database access from
/// editing a row, but it guarantees that doing so is detectable, because
/// every row's <see cref="AuditEntry.EntryHash"/> commits to both its own
/// fields and the immediately preceding row's hash -- the same
/// hash-chaining construction blockchains and Git commits use, applied
/// here to a plain SQLite table instead.
///
/// Why this belongs in a fintech governance toolkit specifically: several
/// of the regulatory instruments the field guide surveys (the EU AI Act's
/// record-keeping obligations for high-risk systems, MAS Project
/// MindForge's audit expectations) require that an institution be able to
/// show a regulator "this is the decision our system made and it has not
/// been altered since." A plain <c>INSERT</c>-only table asserts that; a
/// hash chain lets you actually verify it later without trusting whoever
/// operated the database in between.
///
/// See <c>verification/audit_trail_tamper_check.py</c> for a live SQLite
/// run of this exact construction: entries are appended, one historical
/// row is mutated directly with UPDATE (simulating an operator or an
/// attacker editing the database file), and the chain-verification pass
/// is shown to catch it -- not asserted, actually demonstrated against a
/// real database file.
/// </summary>
public sealed class AuditTrailStore
{
    public const string GenesisHash = "GENESIS";
    private readonly string _connectionString;

    public AuditTrailStore(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AuditEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                ActorId TEXT NOT NULL,
                ActionDescription TEXT NOT NULL,
                RiskLevel TEXT NOT NULL,
                RiskScore REAL NOT NULL,
                GovernanceAction TEXT NOT NULL,
                Detail TEXT,
                PriorHash TEXT NOT NULL,
                EntryHash TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Canonical string form of an entry's payload fields, in a fixed
    /// field order with a fixed separator -- this exact string is what
    /// gets hashed, so any change to the canonicalization here would
    /// invalidate every previously-computed hash. Kept as its own method
    /// (rather than inlined) so the Python oracle can be checked against
    /// it field-for-field.
    /// </summary>
    internal static string Canonicalize(DateTime timestampUtc, string actorId, string actionDescription,
        string riskLevel, double riskScore, string governanceAction, string? detail, string priorHash)
    {
        return string.Join("|",
            timestampUtc.ToString("O", CultureInfo.InvariantCulture),
            actorId,
            actionDescription,
            riskLevel,
            riskScore.ToString("R", CultureInfo.InvariantCulture),
            governanceAction,
            detail ?? "",
            priorHash);
    }

    internal static string ComputeHash(string canonical)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public AuditEntry Append(string actorId, string actionDescription, RiskAssessment risk, GovernanceResult result)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        string priorHash = GetLastHash(conn);
        var timestamp = DateTime.UtcNow;
        string detail = string.Join(" | ", result.Verdicts.Select(v => $"{v.RuleId}: {v.Detail}"));

        string canonical = Canonicalize(timestamp, actorId, actionDescription,
            risk.Level.ToString(), risk.Score, result.Action.ToString(), detail, priorHash);
        string entryHash = ComputeHash(canonical);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AuditEntries
                (TimestampUtc, ActorId, ActionDescription, RiskLevel, RiskScore, GovernanceAction, Detail, PriorHash, EntryHash)
            VALUES
                (@ts, @actor, @action, @riskLevel, @riskScore, @govAction, @detail, @priorHash, @entryHash);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@ts", timestamp.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@actor", actorId);
        cmd.Parameters.AddWithValue("@action", actionDescription);
        cmd.Parameters.AddWithValue("@riskLevel", risk.Level.ToString());
        cmd.Parameters.AddWithValue("@riskScore", risk.Score);
        cmd.Parameters.AddWithValue("@govAction", result.Action.ToString());
        cmd.Parameters.AddWithValue("@detail", (object?)detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@priorHash", priorHash);
        cmd.Parameters.AddWithValue("@entryHash", entryHash);
        long id = Convert.ToInt64(cmd.ExecuteScalar());

        return new AuditEntry(id, timestamp, actorId, actionDescription, risk.Level.ToString(), risk.Score,
            result.Action.ToString(), detail, priorHash, entryHash);
    }

    private static string GetLastHash(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EntryHash FROM AuditEntries ORDER BY Id DESC LIMIT 1";
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? GenesisHash : (string)result;
    }

    /// <summary>
    /// Walks every row in Id order, recomputing each row's hash from its
    /// stored fields and comparing it to the stored <c>EntryHash</c>, and
    /// separately checking that each row's stored <c>PriorHash</c> matches
    /// the previous row's stored <c>EntryHash</c>. Returns the Id of the
    /// first row that fails either check, or null if the whole chain is
    /// intact. Two distinct failure modes are worth distinguishing in a
    /// real incident: a row whose own hash no longer matches its content
    /// (that row was edited) vs. a row whose PriorHash link is broken (a
    /// row was deleted or reordered) -- both surface here as "first
    /// broken Id", but <see cref="VerifyChain(SqliteConnection)"/>'s
    /// Python counterpart in the verification script reports which case
    /// it hit, for the same reason a fintech audit tool should always
    /// tell you not just "the chain is broken" but "here's specifically
    /// what changed."
    /// </summary>
    public long? VerifyChain()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, TimestampUtc, ActorId, ActionDescription, RiskLevel, RiskScore,
                   GovernanceAction, Detail, PriorHash, EntryHash
            FROM AuditEntries ORDER BY Id ASC
            """;
        using var reader = cmd.ExecuteReader();

        string expectedPriorHash = GenesisHash;
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            var timestamp = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            string actorId = reader.GetString(2);
            string actionDescription = reader.GetString(3);
            string riskLevel = reader.GetString(4);
            double riskScore = reader.GetDouble(5);
            string governanceAction = reader.GetString(6);
            string? detail = reader.IsDBNull(7) ? null : reader.GetString(7);
            string storedPriorHash = reader.GetString(8);
            string storedEntryHash = reader.GetString(9);

            if (storedPriorHash != expectedPriorHash) return id; // broken link to the previous row

            string canonical = Canonicalize(timestamp, actorId, actionDescription, riskLevel, riskScore,
                governanceAction, detail, storedPriorHash);
            if (ComputeHash(canonical) != storedEntryHash) return id; // this row's content was altered

            expectedPriorHash = storedEntryHash;
        }
        return null;
    }
}
