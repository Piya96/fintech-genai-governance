using GenAiGovernance.Core.Rules;
using GenAiGovernance.Domain;

namespace GenAiGovernance.Core;

/// <summary>
/// Combines the risk assessment and the three mechanical content checks
/// into one auditable decision. This class is the "Engineering layer"
/// enforcement point from the field guide's Section 02 four-layer
/// architecture (Policy sets the rules; Engineering, here, is the code
/// that actually stops a violating action from reaching a customer or an
/// external system, no matter what the Policy layer's text says the model
/// should do).
///
/// Order matters and is fixed, not configurable, on purpose -- letting
/// each call site reorder these checks is how a mechanical governance
/// layer quietly becomes a soft, negotiable one:
///
/// 1. Risk gate first. If the paired <see cref="RiskAssessment"/> is at or
///    above the human-approval threshold, this method returns
///    <see cref="GovernanceAction.RequireHumanApproval"/> immediately.
///    Content checks still run (see below) so the audit entry captures
///    what the model *would* have said, but nothing is released.
/// 2. Restricted-topic check. Any match blocks outright -- see
///    <see cref="RestrictedTopicGate"/> for why there's no rewrite path.
/// 3. PII redaction, then disclaimer injection, in that order (a
///    disclaimer should never itself be scanned for PII patterns it
///    doesn't contain, but running redaction first means if it ever did,
///    the injected disclaimer text is not clobbered by a later redaction
///    pass).
/// </summary>
public static class GovernanceGate
{
    public static GovernanceResult Evaluate(string proposedOutput, RiskAssessment risk)
    {
        var verdicts = new List<GovernanceVerdict>();
        string text = proposedOutput;

        var topicVerdicts = RestrictedTopicGate.Check(text);
        verdicts.AddRange(topicVerdicts);

        var piiVerdicts = PiiRedactor.Redact(ref text);
        verdicts.AddRange(piiVerdicts);

        var disclaimerVerdicts = DisclaimerInjector.Inject(ref text);
        verdicts.AddRange(disclaimerVerdicts);

        // Risk gate outranks everything else: even a topic-clean,
        // PII-free, fully-disclaimed response to a high-autonomy,
        // irreversible, wide-blast-radius action does not auto-execute.
        if (risk.Level == RiskLevel.RequiresHumanApproval)
        {
            return new GovernanceResult(GovernanceAction.RequireHumanApproval, verdicts, ReleasedText: null);
        }

        if (topicVerdicts.Count > 0)
        {
            return new GovernanceResult(GovernanceAction.Block, verdicts, ReleasedText: null);
        }

        var action = (piiVerdicts.Count > 0 || disclaimerVerdicts.Count > 0)
            ? GovernanceAction.AllowWithModification
            : GovernanceAction.Allow;

        return new GovernanceResult(action, verdicts, ReleasedText: text);
    }
}
