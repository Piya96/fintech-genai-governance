#!/usr/bin/env python3
"""
A line-for-line Python mirror of AgenticRiskScorer.cs's arithmetic (mean
of four 1-3 factors, threshold 2.5 inclusive), used to exhaustively check
the full 3^4 = 81-point input space -- cheap to do completely here exactly
because the input space is that small, and worth doing completely because
"eyeball a few examples" is how an off-by-one at the threshold boundary
(is 2.5 itself auto-executable or not?) goes unnoticed.

Honest limitation, stated plainly: there is no .NET SDK in this sandbox,
so this script cannot invoke the actual C# AgenticRiskScorer.Score and
diff its output against this oracle the way
verification/test_sqlite_batch_verify.py did for the Matching Engine
repo (SQL Server batch scoring vs. Python oracle, both actually
executed). What this script instead verifies is the SPECIFICATION: that
the formula as written down here -- and reviewed line-by-line against
AgenticRiskScorer.cs's Score() method -- behaves the way the field
guide's Section 02 describes across every possible input, including the
boundary cases a spot check would likely miss.
"""
from itertools import product

THRESHOLD = 2.5


def score(autonomy: int, reversibility: int, blast_radius: int, mutability: int) -> tuple[float, str]:
    mean = (autonomy + reversibility + blast_radius + mutability) / 4.0
    level = "RequiresHumanApproval" if mean >= THRESHOLD else "AutoExecutable"
    return mean, level


def run():
    combos = list(product([1, 2, 3], repeat=4))
    assert len(combos) == 81, f"Expected 81 combinations, got {len(combos)}"

    counts = {"AutoExecutable": 0, "RequiresHumanApproval": 0}
    boundary_cases = []
    for a, r, b, m in combos:
        mean, level = score(a, r, b, m)
        counts[level] += 1
        if mean == THRESHOLD:
            boundary_cases.append((a, r, b, m, mean, level))

    print(f"Exhaustive check over all {len(combos)} (Autonomy, Reversibility, BlastRadius, Mutability) combinations:")
    print(f"  AutoExecutable:        {counts['AutoExecutable']}")
    print(f"  RequiresHumanApproval: {counts['RequiresHumanApproval']}")
    print()

    print(f"Boundary cases where mean == {THRESHOLD} exactly (must resolve to RequiresHumanApproval -- 'inclusive' threshold):")
    for a, r, b, m, mean, level in boundary_cases:
        assert level == "RequiresHumanApproval", f"Threshold should be inclusive: {(a, r, b, m)} -> {level}"
        print(f"  ({a},{r},{b},{m}) mean={mean} -> {level}")
    print(f"  {len(boundary_cases)} boundary cases found, all correctly RequiresHumanApproval.")
    print()

    # Sanity checks on the extremes, matching AgenticRiskScorerTests.cs exactly.
    assert score(1, 1, 1, 1) == (1.0, "AutoExecutable")
    assert score(3, 3, 3, 3) == (3.0, "RequiresHumanApproval")
    assert score(2, 2, 2, 2) == (2.0, "AutoExecutable")
    assert score(3, 3, 2, 2) == (2.5, "RequiresHumanApproval")
    print("Extreme-value and known-boundary assertions (mirroring AgenticRiskScorerTests.cs) PASSED.")

    # Monotonicity: increasing any single factor should never decrease the
    # resulting mean (a sanity property the C# formula's plain arithmetic
    # mean guarantees, but worth confirming exhaustively rather than by
    # inspection alone).
    violations = 0
    for a, r, b, m in combos:
        base_mean, _ = score(a, r, b, m)
        for i in range(4):
            factors = [a, r, b, m]
            if factors[i] == 3:
                continue
            factors[i] += 1
            bumped_mean, _ = score(*factors)
            if bumped_mean < base_mean:
                violations += 1
    assert violations == 0, f"Found {violations} monotonicity violations"
    print("Monotonicity check (bumping any single factor never lowers the score) PASSED across all 81 points.")


if __name__ == "__main__":
    run()
