using System.Text.Json;

namespace GenAiGovernance.Core;

/// <summary>
/// One entry from <c>data/regulatory_corpus.json</c>: a short, paraphrased
/// summary of a real regulatory instrument or framework surveyed in the
/// Tier 5 field guide, never the instrument's actual legal text. Every
/// entry's <see cref="Source"/> says so explicitly -- this corpus exists
/// to demonstrate retrieve-then-explain mechanics, not to serve as a
/// citable legal reference, and <see cref="ComplianceExplainer"/>'s output
/// repeats that caveat for the same reason.
/// </summary>
public sealed record RegulatoryDocument(string Id, string Title, string Source, string Text);

public static class RegulatoryCorpus
{
    public static IReadOnlyList<RegulatoryDocument> Load(string jsonPath)
    {
        string json = File.ReadAllText(jsonPath);
        var docs = JsonSerializer.Deserialize<List<RegulatoryDocument>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        return docs ?? throw new InvalidOperationException($"Could not parse corpus at {jsonPath}");
    }
}
