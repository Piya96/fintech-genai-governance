using GenAiGovernance.Core;
using GenAiGovernance.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GenAiGovernance.Tests;

public class AuditTrailStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static (RiskAssessment, GovernanceResult) SampleDecision() =>
        (AgenticRiskScorer.Score(new RiskFactors(1, 1, 1, 1)),
         GovernanceGate.Evaluate("Your balance is $500.", AgenticRiskScorer.Score(new RiskFactors(1, 1, 1, 1))));

    [Fact]
    public void FirstEntry_ChainsFromGenesis()
    {
        var store = new AuditTrailStore(_dbPath);
        var (risk, result) = SampleDecision();

        var entry = store.Append("actor-1", "balance inquiry", risk, result);

        Assert.Equal(AuditTrailStore.GenesisHash, entry.PriorHash);
        Assert.NotEqual(AuditTrailStore.GenesisHash, entry.EntryHash);
    }

    [Fact]
    public void EachEntry_ChainsToPreviousEntryHash()
    {
        var store = new AuditTrailStore(_dbPath);
        var (risk, result) = SampleDecision();

        var first = store.Append("actor-1", "action A", risk, result);
        var second = store.Append("actor-1", "action B", risk, result);

        Assert.Equal(first.EntryHash, second.PriorHash);
    }

    [Fact]
    public void VerifyChain_PassesOnUntamperedTrail()
    {
        var store = new AuditTrailStore(_dbPath);
        var (risk, result) = SampleDecision();
        for (int i = 0; i < 5; i++) store.Append("actor-1", $"action {i}", risk, result);

        Assert.Null(store.VerifyChain());
    }

    [Fact]
    public void VerifyChain_DetectsDirectRowMutation()
    {
        var store = new AuditTrailStore(_dbPath);
        var (risk, result) = SampleDecision();
        store.Append("actor-1", "action A", risk, result);
        var toTamper = store.Append("actor-1", "action B", risk, result);
        store.Append("actor-1", "action C", risk, result);

        // Simulate an operator (or attacker) editing a historical row
        // directly via SQL, bypassing AuditTrailStore.Append entirely.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE AuditEntries SET ActionDescription = 'action B (edited)' WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", toTamper.Id);
            cmd.ExecuteNonQuery();
        }

        long? brokenAt = store.VerifyChain();
        Assert.Equal(toTamper.Id, brokenAt);
    }
}
