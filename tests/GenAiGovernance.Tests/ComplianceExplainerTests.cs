using GenAiGovernance.Core;
using Xunit;

namespace GenAiGovernance.Tests;

public class ComplianceExplainerTests
{
    private static ComplianceExplainer BuildExplainer()
    {
        var corpus = RegulatoryCorpus.Load(FindCorpusPath());
        return new ComplianceExplainer(corpus);
    }

    // Walks up from the test assembly's output directory to find the repo
    // root's data/ folder, since the test project doesn't copy the corpus
    // to its own output directory the way GenAiGovernance.Demo does.
    private static string FindCorpusPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "regulatory_corpus.json")))
        {
            dir = dir.Parent;
        }
        return dir is null
            ? throw new FileNotFoundException("Could not locate data/regulatory_corpus.json above the test output directory.")
            : Path.Combine(dir.FullName, "data", "regulatory_corpus.json");
    }

    [Fact]
    public void CreditScoringQuery_RetrievesHighRiskClassificationDocument()
    {
        var explainer = BuildExplainer();
        var results = explainer.Explain("Why is our credit scoring model considered high-risk?");

        Assert.NotEmpty(results);
        Assert.Equal("EU-AIA-HR-01", results[0].Document.Id);
    }

    [Fact]
    public void ProtectedCharacteristicQuery_RetrievesFairLendingDocument()
    {
        var explainer = BuildExplainer();
        var results = explainer.Explain("Can we decline a loan application based on national origin?");

        Assert.NotEmpty(results);
        Assert.Equal("FAIR-LENDING-01", results[0].Document.Id);
    }

    [Fact]
    public void IrrelevantQuery_ReturnsNothing()
    {
        var explainer = BuildExplainer();
        var results = explainer.Explain("What is the weather forecast for tomorrow?");

        Assert.Empty(results);
    }

    [Fact]
    public void ResultsAreOrderedBySimilarityDescending()
    {
        var explainer = BuildExplainer();
        var results = explainer.Explain("automated AI decisioning human oversight risk", topK: 5);

        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Similarity >= results[i].Similarity);
        }
    }
}
