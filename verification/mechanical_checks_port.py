"""
A hand-port of the two format-based mechanical checks from
src/GenAiGovernance.Core/Rules/PiiRedactor.cs and RestrictedTopicGate.cs,
kept in sync with those files manually (no shared source -- there's no
.NET SDK in this sandbox to run the real C#, so this Python mirror is what
verification/governance_task_decoupling_demo.py and the PII-pattern half
of the test suite below actually execute against). Every regex here is a
literal transcription of the corresponding C# pattern; if you change one
side, change the other.
"""
import re

PII_PATTERNS = [
    ("PII-EMAIL", re.compile(r"\b[\w.+-]+@[\w-]+\.[\w.-]+\b")),
    ("PII-IBAN", re.compile(r"\b[A-Z]{2}\d{2}[A-Z0-9]{10,30}\b")),
    ("PII-SSN", re.compile(r"\b\d{3}-\d{2}-\d{4}\b")),
    ("PII-CARD", re.compile(r"\b(?:\d[ -]?){13,16}\b")),
    ("PII-ACCOUNT", re.compile(r"\b\d{8,17}\b")),
]

GUARANTEE_PATTERNS = [
    re.compile(r"\b(guarantee(d|s)?|risk[- ]?free|can'?t lose|no risk)\b.{0,40}\b(returns?|profits?|gains?|yields?)\b", re.IGNORECASE),
    re.compile(r"\b(returns?|profits?|gains?|yields?)\b.{0,40}\b(guarantee(d|s)?|risk[- ]?free|can'?t lose|no risk)\b", re.IGNORECASE),
]


def mechanical_redact(text: str) -> tuple[str, list[str]]:
    """Format-based PII redaction: fires on every match, regardless of
    what word (if any) precedes it. Returns (redacted_text, rule_ids_fired)."""
    fired = []
    for rule_id, pattern in PII_PATTERNS:
        if pattern.search(text):
            text = pattern.sub(f"[REDACTED]", text)
            fired.append(rule_id)
    return text, fired


def mechanical_catches_violation(text: str) -> bool:
    """True if the mechanical checks would flag this text at all (PII or
    a guaranteed-return claim) -- used by the decoupling demo as the
    ground-truth-independent 'did governance catch this' signal."""
    _, pii_fired = mechanical_redact(text)
    guarantee_fired = any(p.search(text) for p in GUARANTEE_PATTERNS)
    return bool(pii_fired) or guarantee_fired


def naive_text_only_catches_violation(text: str) -> bool:
    """
    A simplified stand-in for a text-only / prompt-based governance
    approach: a model instructed "don't share account numbers or SSNs,
    and don't guarantee returns" that relies on the presence of an
    explicit, nearby trigger phrase to recognize what it's looking at,
    rather than checking the actual shape of the output.

    This is NOT a reproduction of any paper's own reported failure rate
    -- arXiv 2605.14744 argues for mechanical enforcement precisely
    because text-only/prompt-based governance is inconsistent under
    paraphrase, but this function does not claim to replicate that
    paper's specific numbers. It's a deliberately simple illustration of
    *why* that argument holds: catching "my SSN is 123-45-6789" (an
    explicit label right before the number) but missing "reach the
    account at 123-45-6789 for verification" (the same shape, no
    labeling trigger word immediately before it) is exactly the kind of
    surface-cue brittleness a format-based regex does not have, because
    it never looks at the words around the match at all.
    """
    trigger_then_number = re.compile(
        r"\b(SSN|social security|account number|acct\.?\s*#?)\b\s*(is|:|=)?\s*[\d -]{8,}",
        re.IGNORECASE)
    trigger_then_guarantee = re.compile(r"\bI\s+(guarantee|promise)\b", re.IGNORECASE)
    return bool(trigger_then_number.search(text)) or bool(trigger_then_guarantee.search(text))
