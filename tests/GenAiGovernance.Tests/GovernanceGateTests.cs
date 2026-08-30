using GenAiGovernance.Core;
using GenAiGovernance.Core.Rules;
using GenAiGovernance.Domain;
using Xunit;

namespace GenAiGovernance.Tests;

public class PiiRedactorTests
{
    [Fact]
    public void RedactsEmailAndAccountNumber()
    {
        string text = "Contact us at support@bank.com regarding account 00293841057.";
        var verdicts = PiiRedactor.Redact(ref text);

        Assert.Contains(verdicts, v => v.RuleId == "PII-EMAIL");
        Assert.Contains(verdicts, v => v.RuleId == "PII-ACCOUNT");
        Assert.DoesNotContain("support@bank.com", text);
        Assert.DoesNotContain("00293841057", text);
    }

    [Fact]
    public void LeavesCleanTextUnchanged()
    {
        string text = "Your request has been processed successfully.";
        var verdicts = PiiRedactor.Redact(ref text);

        Assert.Empty(verdicts);
        Assert.Equal("Your request has been processed successfully.", text);
    }

    [Fact]
    public void RedactsSsnShapeBeforeGenericAccountPattern()
    {
        string text = "SSN on file: 123-45-6789.";
        var verdicts = PiiRedactor.Redact(ref text);

        Assert.Contains(verdicts, v => v.RuleId == "PII-SSN");
        Assert.DoesNotContain("123-45-6789", text);
    }
}

public class RestrictedTopicGateTests
{
    [Theory]
    [InlineData("I can guarantee a 12% return with no risk.")]
    [InlineData("This fund offers a risk-free return of 8% annually.")]
    public void FlagsGuaranteedReturnLanguage(string text)
    {
        var verdicts = RestrictedTopicGate.Check(text);
        Assert.Contains(verdicts, v => v.RuleId is "TOPIC-GUARANTEE" or "TOPIC-GUARANTEE-REV");
    }

    [Fact]
    public void FlagsProtectedCharacteristicLendingLanguage()
    {
        string text = "We should decline this application because of the applicant's national origin.";
        var verdicts = RestrictedTopicGate.Check(text);
        Assert.Contains(verdicts, v => v.RuleId == "TOPIC-PROTECTED-CLASS");
    }

    [Fact]
    public void AllowsOrdinaryInvestmentLanguageWithoutGuarantees()
    {
        string text = "Historically, diversified portfolios have shown moderate long-term growth, with no guarantees.";
        // "no guarantees" appears, but with no return/profit/gain/yield
        // word within 40 characters of it -- an honest risk disclosure
        // like this one should not false-positive as a guaranteed-return
        // claim just because the word "guarantees" is present somewhere.
        var verdicts = RestrictedTopicGate.Check(text);
        Assert.DoesNotContain(verdicts, v => v.RuleId is "TOPIC-GUARANTEE" or "TOPIC-GUARANTEE-REV");
    }
}

public class DisclaimerInjectorTests
{
    [Fact]
    public void AppendsPerformanceDisclaimerOnce()
    {
        string text = "This fund's historical performance shows 8% annualized returns.";
        var verdicts = DisclaimerInjector.Inject(ref text);

        Assert.Single(verdicts.Where(v => v.RuleId == "DISC-PERFORMANCE"));
        Assert.Contains("Past performance is not indicative", text);

        // Idempotent: running it again on the already-modified text
        // should not append a second copy.
        var secondPass = DisclaimerInjector.Inject(ref text);
        Assert.DoesNotContain(secondPass, v => v.RuleId == "DISC-PERFORMANCE");
    }
}

public class GovernanceGateTests
{
    [Fact]
    public void CleanLowRiskOutput_IsAllowedUnchanged()
    {
        var risk = AgenticRiskScorer.Score(new RiskFactors(1, 1, 1, 1));
        var result = GovernanceGate.Evaluate("Your balance is $500.", risk);

        Assert.Equal(GovernanceAction.Allow, result.Action);
        Assert.Equal("Your balance is $500.", result.ReleasedText);
    }

    [Fact]
    public void PiiInLowRiskOutput_IsAllowedWithModification()
    {
        var risk = AgenticRiskScorer.Score(new RiskFactors(1, 1, 1, 1));
        var result = GovernanceGate.Evaluate("Your account is 00293841057.", risk);

        Assert.Equal(GovernanceAction.AllowWithModification, result.Action);
        Assert.DoesNotContain("00293841057", result.ReleasedText);
    }

    [Fact]
    public void RestrictedTopic_IsBlockedRegardlessOfRisk()
    {
        var risk = AgenticRiskScorer.Score(new RiskFactors(1, 1, 1, 1)); // low risk
        var result = GovernanceGate.Evaluate("I guarantee a 20% return with no risk.", risk);

        Assert.Equal(GovernanceAction.Block, result.Action);
        Assert.Null(result.ReleasedText);
    }

    [Fact]
    public void HighRiskAction_RequiresHumanApproval_EvenWithCleanText()
    {
        var risk = AgenticRiskScorer.Score(new RiskFactors(3, 3, 3, 3));
        var result = GovernanceGate.Evaluate("Applying a rate change across the lending tier.", risk);

        Assert.Equal(GovernanceAction.RequireHumanApproval, result.Action);
        Assert.Null(result.ReleasedText);
    }

    [Fact]
    public void RiskGateOutranksBlock_BothRecordedInVerdicts()
    {
        // A high-risk action whose text also trips the restricted-topic
        // gate: the final action must be RequireHumanApproval (risk
        // outranks block), but the topic verdict should still appear in
        // the verdict list for the audit trail.
        var risk = AgenticRiskScorer.Score(new RiskFactors(3, 3, 3, 3));
        var result = GovernanceGate.Evaluate("I guarantee a 20% return with no risk.", risk);

        Assert.Equal(GovernanceAction.RequireHumanApproval, result.Action);
        Assert.Contains(result.Verdicts, v => v.RuleId is "TOPIC-GUARANTEE" or "TOPIC-GUARANTEE-REV");
    }
}
