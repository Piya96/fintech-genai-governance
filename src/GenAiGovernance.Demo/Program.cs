using GenAiGovernance.Core;
using GenAiGovernance.Domain;

// A walkthrough of five candidate actions a fictional fintech customer-service
// agent might propose, each chosen to exercise a different path through
// AgenticRiskScorer + GovernanceGate + AuditTrailStore + ComplianceExplainer.
// Nothing here is FinFind- or employer-specific -- these are generic
// "retail-banking chatbot" scenarios built to demonstrate the toolkit, the
// same zero-employer-specifics rule the rest of this portfolio follows.

string dbPath = Path.Combine(AppContext.BaseDirectory, "audit_trail_demo.db");
if (File.Exists(dbPath)) File.Delete(dbPath); // fresh run each time

var auditTrail = new AuditTrailStore(dbPath);

// GenAiGovernance.Demo.csproj copies data/regulatory_corpus.json next to
// the built binary (CopyToOutputDirectory), so this resolves the same way
// under `dotnet run` and a published binary alike.
string corpusPath = Path.Combine(AppContext.BaseDirectory, "regulatory_corpus.json");
var corpus = RegulatoryCorpus.Load(corpusPath);
var explainer = new ComplianceExplainer(corpus);

var scenarios = new[]
{
    new Scenario(
        "Answer a balance inquiry",
        "Your current balance is $4,821.09 as of this morning.",
        new RiskFactors(Autonomy: 1, Reversibility: 1, BlastRadius: 1, Mutability: 1),
        "Why would this be gated?"),

    new Scenario(
        "Explain historical fund performance",
        "This fund's historical performance shows 8% annualized returns over five years.",
        new RiskFactors(Autonomy: 1, Reversibility: 1, BlastRadius: 1, Mutability: 1),
        "Why does this response include a disclaimer?"),

    new Scenario(
        "Draft a response revealing account details",
        "Sure, your account number is 00293841057 and you can reach us at agent@ourbank.com.",
        new RiskFactors(Autonomy: 2, Reversibility: 1, BlastRadius: 1, Mutability: 1),
        "Why was PII redacted from this response?"),

    new Scenario(
        "Promise guaranteed investment returns",
        "I can guarantee a 12% return on this investment with no risk to your principal.",
        new RiskFactors(Autonomy: 2, Reversibility: 2, BlastRadius: 1, Mutability: 1),
        "Why was this response blocked instead of redacted?"),

    new Scenario(
        "Autonomously adjust a systemic pricing parameter",
        "Applying a -0.25% rate adjustment across the small-business lending tier, effective immediately.",
        new RiskFactors(Autonomy: 3, Reversibility: 3, BlastRadius: 3, Mutability: 3),
        "Why does this require a human before it executes, even though the text itself is clean?"),
};

foreach (var scenario in scenarios)
{
    Console.WriteLine(new string('=', 78));
    Console.WriteLine($"Scenario: {scenario.Name}");
    Console.WriteLine($"Proposed output: {scenario.ProposedOutput}");

    var risk = AgenticRiskScorer.Score(scenario.Factors);
    Console.WriteLine($"Risk: {risk.Level} (score {risk.Score:0.00}) -- {risk.Rationale}");

    var result = GovernanceGate.Evaluate(scenario.ProposedOutput, risk);
    Console.WriteLine($"Governance action: {result.Action}");
    foreach (var verdict in result.Verdicts)
    {
        Console.WriteLine($"  [{verdict.RuleId}] {verdict.Detail}");
    }
    Console.WriteLine(result.ReleasedText is null
        ? "  -> Nothing released to the customer."
        : $"  -> Released: {result.ReleasedText}");

    var entry = auditTrail.Append(actorId: "demo-agent-01", actionDescription: scenario.Name, risk, result);
    Console.WriteLine($"Audit entry #{entry.Id} written. EntryHash={entry.EntryHash[..12]}...");

    Console.WriteLine($"\nCompliance explanation for: \"{scenario.ExplainQuestion}\"");
    foreach (var explanation in explainer.Explain(scenario.ExplainQuestion))
    {
        Console.WriteLine($"  ({explanation.Similarity:0.000}) {explanation.Document.Title} -- {explanation.Document.Source}");
    }
    Console.WriteLine();
}

Console.WriteLine(new string('=', 78));
long? brokenAt = auditTrail.VerifyChain();
Console.WriteLine(brokenAt is null
    ? $"Audit trail integrity check: PASSED ({scenarios.Length} entries, unbroken hash chain)."
    : $"Audit trail integrity check: FAILED at entry #{brokenAt}.");

record Scenario(string Name, string ProposedOutput, RiskFactors Factors, string ExplainQuestion);
