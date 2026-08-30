using System.Text.RegularExpressions;
using GenAiGovernance.Domain;

namespace GenAiGovernance.Core.Rules;

/// <summary>
/// Blocks output containing phrasing that no financial institution's
/// customer-facing agent should ever emit, regardless of context --
/// guaranteed-return language (securities regulators worldwide treat
/// "guaranteed returns" claims as a specific, named violation; this is the
/// plain-language version of the US SEC's proposed AI conflict-of-interest
/// rule the field guide cites in Section 01), and lending language that
/// references a protected characteristic (the mechanical equivalent of a
/// fair-lending / ECOA-style check).
///
/// Unlike <see cref="PiiRedactor"/>, there's no safe rewrite for these --
/// you can't "redact" your way out of an illegal guarantee, so a match
/// here is always <see cref="Domain.GovernanceAction.Block"/>, never
/// <see cref="Domain.GovernanceAction.AllowWithModification"/>. That
/// distinction (redact vs. block) is itself a governance decision worth
/// naming explicitly rather than leaving implicit in whichever gate
/// happens to run first -- see <see cref="GovernanceGate"/>.
///
/// Keyword/phrase matching is a real, named limitation: it will miss
/// paraphrases and catch some false positives. That's the honest trade
/// the field guide's Section 03 names for mechanical checks generally --
/// lower recall than an LLM-based judge, but the matches it does report
/// are ones no amount of prompt-level pressure can talk it out of. A
/// production system would layer this under, not instead of, the model's
/// own instruction-following.
/// </summary>
public static class RestrictedTopicGate
{
    private static readonly (string RuleId, Regex Pattern, string Category)[] Patterns =
    {
        ("TOPIC-GUARANTEE", new Regex(
            @"\b(guarantee(d|s)?|risk[- ]?free|can'?t lose|no risk)\b.{0,40}\b(returns?|profits?|gains?|yields?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "Guaranteed-return language"),

        ("TOPIC-GUARANTEE-REV", new Regex(
            @"\b(returns?|profits?|gains?|yields?)\b.{0,40}\b(guarantee(d|s)?|risk[- ]?free|can'?t lose|no risk)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "Guaranteed-return language"),

        ("TOPIC-INSIDER", new Regex(
            @"\b(insider (info|information|tip)|material non-?public information|trade (before|ahead of) the announcement)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "Insider-trading-adjacent language"),

        ("TOPIC-PROTECTED-CLASS", new Regex(
            @"\b(deny|reject|decline|lower (the )?limit)\b.{0,40}\b(because|since|due to)\b.{0,40}\b(race|religion|national origin|gender|marital status|age|disability)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "Fair-lending / protected-characteristic language"),
    };

    /// <summary>
    /// Returns one <see cref="GovernanceVerdict"/> per pattern that
    /// matched. Text is never transformed here (see class doc for why);
    /// <c>TransformedText</c> always equals the input.
    /// </summary>
    public static IReadOnlyList<GovernanceVerdict> Check(string text)
    {
        var verdicts = new List<GovernanceVerdict>();
        foreach (var (ruleId, pattern, category) in Patterns)
        {
            if (!pattern.IsMatch(text)) continue;
            verdicts.Add(new GovernanceVerdict(
                Passed: false,
                RuleId: ruleId,
                Detail: $"{category} detected -- no safe rewrite; output must be blocked.",
                TransformedText: text));
        }
        return verdicts;
    }
}
