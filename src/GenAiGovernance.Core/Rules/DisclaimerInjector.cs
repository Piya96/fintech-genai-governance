using System.Text.RegularExpressions;
using GenAiGovernance.Domain;

namespace GenAiGovernance.Core.Rules;

/// <summary>
/// Appends a required disclaimer whenever output mentions a topic that
/// regulators require one for, regardless of whether the model remembered
/// to include it itself. This is the mildest of the three mechanical
/// gates -- it never blocks and never removes content, only appends -- but
/// it's included for the same reason PiiRedactor is: a model instructed to
/// "always include the standard disclaimer when discussing investment
/// performance" will sometimes forget under an unusual phrasing of the
/// user's question. This check doesn't care about phrasing; it matches
/// the topic in the model's own output and appends deterministically.
///
/// Each rule fires at most once per response (checked via
/// <see cref="string.Contains(string)"/> on the disclaimer text itself,
/// so re-running this gate on already-processed text is idempotent --
/// relevant because <see cref="GovernanceGate"/> may run gates in a
/// pipeline where order matters for redaction but must not cause a
/// disclaimer to be appended twice).
/// </summary>
public static class DisclaimerInjector
{
    private static readonly (string RuleId, Regex Trigger, string Disclaimer)[] Rules =
    {
        ("DISC-PERFORMANCE",
            new Regex(@"\b(historical|past)\b.{0,20}\b(performance|return)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Past performance is not indicative of future results."),

        ("DISC-NOT-ADVICE",
            new Regex(@"\b(invest(ing|ment)?|portfolio|asset allocation|stocks?|bonds?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "This information is educational only and is not personalized financial, tax, or legal advice."),

        ("DISC-RATE",
            new Regex(@"\b(interest rate|APR|APY)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Rates shown are subject to change and may not reflect your final offer."),
    };

    public static IReadOnlyList<GovernanceVerdict> Inject(ref string text)
    {
        var verdicts = new List<GovernanceVerdict>();
        foreach (var (ruleId, trigger, disclaimer) in Rules)
        {
            if (!trigger.IsMatch(text)) continue;
            if (text.Contains(disclaimer, StringComparison.Ordinal)) continue; // idempotent

            text = $"{text}\n\n[{disclaimer}]";
            verdicts.Add(new GovernanceVerdict(
                Passed: false,
                RuleId: ruleId,
                Detail: $"Appended required disclaimer for topic trigger '{trigger}'.",
                TransformedText: text));
        }
        return verdicts;
    }
}
