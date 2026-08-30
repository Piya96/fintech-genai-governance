using System.Text.RegularExpressions;
using GenAiGovernance.Domain;

namespace GenAiGovernance.Core.Rules;

/// <summary>
/// Regex-based PII redaction -- the clearest example in this toolkit of
/// "mechanical enforcement" as arXiv 2605.14744 defines it: a check that
/// runs on the agent's actual output string, outside the model's own
/// interpretive loop, so it cannot be argued out of firing by a
/// sufficiently persuasive prompt. A model can be instructed not to reveal
/// account numbers and comply 98% of the time; this class doesn't ask the
/// model anything -- it scans the string the model already produced.
///
/// This is deliberately narrow and deliberately dumb: five patterns, no
/// ML, no embeddings, no LLM-as-judge. That's the entire argument for why
/// it belongs in the mechanical layer rather than the model's system
/// prompt -- a regex either matches or it doesn't, and that's auditable in
/// a way "the model decided not to" never is. See
/// verification/governance_task_decoupling_demo.py for the empirical case
/// this buys you: a mechanical check catches every instance in its test
/// set; a text-only "please don't include PII" instruction does not.
/// </summary>
public static class PiiRedactor
{
    private static readonly (string RuleId, Regex Pattern, string Replacement)[] Patterns =
    {
        ("PII-EMAIL", new Regex(@"\b[\w.+-]+@[\w-]+\.[\w.-]+\b", RegexOptions.Compiled), "[REDACTED-EMAIL]"),

        // IBAN: 2 letters + 2 digits + up to 30 alnum. Checked before the
        // generic account-number pattern below, since an IBAN would
        // otherwise also partially match it.
        ("PII-IBAN", new Regex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{10,30}\b", RegexOptions.Compiled), "[REDACTED-IBAN]"),

        // US SSN shape: 3-2-4 digits, hyphenated.
        ("PII-SSN", new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled), "[REDACTED-SSN]"),

        // 16-digit card number, optionally grouped in 4s with spaces or hyphens.
        ("PII-CARD", new Regex(@"\b(?:\d[ -]?){13,16}\b", RegexOptions.Compiled), "[REDACTED-CARD]"),

        // Generic bank account number: 8-17 bare digits (after the more
        // specific patterns above have already claimed IBANs/cards/SSNs).
        ("PII-ACCOUNT", new Regex(@"\b\d{8,17}\b", RegexOptions.Compiled), "[REDACTED-ACCOUNT]"),
    };

    /// <summary>
    /// Runs every pattern once, in order, over <paramref name="text"/>.
    /// Returns one verdict per pattern that actually matched (patterns
    /// that found nothing are omitted, not returned as a no-op passed
    /// verdict, to keep an audit entry's verdict list focused on what
    /// actually happened).
    /// </summary>
    public static IReadOnlyList<GovernanceVerdict> Redact(ref string text)
    {
        var verdicts = new List<GovernanceVerdict>();
        foreach (var (ruleId, pattern, replacement) in Patterns)
        {
            int hits = pattern.Matches(text).Count;
            if (hits == 0) continue;

            text = pattern.Replace(text, replacement);
            verdicts.Add(new GovernanceVerdict(
                Passed: false,
                RuleId: ruleId,
                Detail: $"{hits} match(es) redacted.",
                TransformedText: text));
        }
        return verdicts;
    }
}
