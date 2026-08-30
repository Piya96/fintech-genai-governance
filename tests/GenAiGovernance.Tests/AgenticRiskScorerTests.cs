using GenAiGovernance.Core;
using GenAiGovernance.Domain;
using Xunit;

namespace GenAiGovernance.Tests;

public class AgenticRiskScorerTests
{
    [Fact]
    public void AllOnes_IsAutoExecutable()
    {
        var result = AgenticRiskScorer.Score(new RiskFactors(1, 1, 1, 1));
        Assert.Equal(RiskLevel.AutoExecutable, result.Level);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public void AllThrees_RequiresHumanApproval()
    {
        var result = AgenticRiskScorer.Score(new RiskFactors(3, 3, 3, 3));
        Assert.Equal(RiskLevel.RequiresHumanApproval, result.Level);
        Assert.Equal(3.0, result.Score);
    }

    [Theory]
    [InlineData(2, 2, 2, 2, RiskLevel.AutoExecutable)] // mean 2.0 < 2.5
    [InlineData(2, 2, 3, 3, RiskLevel.RequiresHumanApproval)] // mean 2.5 == threshold, inclusive
    [InlineData(3, 2, 2, 2, RiskLevel.AutoExecutable)] // mean 2.25 < 2.5
    [InlineData(3, 3, 2, 2, RiskLevel.RequiresHumanApproval)] // mean 2.5
    public void ThresholdIsInclusiveAtExactly2Point5(int a, int r, int b, int m, RiskLevel expected)
    {
        var result = AgenticRiskScorer.Score(new RiskFactors(a, r, b, m));
        Assert.Equal(expected, result.Level);
    }

    [Fact]
    public void RationaleNamesTheDrivingDimension_WhenOneMaxesOut()
    {
        var result = AgenticRiskScorer.Score(new RiskFactors(1, 3, 1, 1)); // mean 1.5 -- not actually over threshold
        Assert.Equal(RiskLevel.AutoExecutable, result.Level);

        var overThreshold = AgenticRiskScorer.Score(new RiskFactors(3, 3, 1, 1)); // mean 2.0 -- still not over
        Assert.Equal(RiskLevel.AutoExecutable, overThreshold.Level);

        var trulyOver = AgenticRiskScorer.Score(new RiskFactors(3, 3, 3, 1)); // mean 2.5
        Assert.Equal(RiskLevel.RequiresHumanApproval, trulyOver.Level);
        Assert.Contains("Reversibility=3", trulyOver.Rationale);
        Assert.Contains("BlastRadius=3", trulyOver.Rationale);
    }

    [Fact]
    public void RejectsOutOfRangeFactor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RiskFactors(0, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RiskFactors(1, 4, 1, 1));
    }
}
