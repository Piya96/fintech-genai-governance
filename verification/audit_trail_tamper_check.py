#!/usr/bin/env python3
"""
A real SQLite run of AuditTrailStore.cs's hash-chain construction,
re-implemented directly against sqlite3 here (not calling into any C#,
since there's no .NET SDK in this sandbox) -- but genuinely exercised
against a real database file, including a genuine SQL UPDATE simulating
an operator or attacker editing a historical row, and a real
verification pass that has to actually notice.

Canonicalization and hashing exactly mirror AuditTrailStore.Canonicalize
and ComputeHash: pipe-joined fields in the fixed order (timestamp, actor,
action, risk level, risk score, governance action, detail, prior hash),
SHA-256 hex digest. If you change the field order or hash algorithm in
one place, change it in the other -- there's no shared source between
the two languages to enforce this automatically.
"""
import hashlib
import os
import sqlite3
import tempfile

GENESIS_HASH = "GENESIS"


def canonicalize(timestamp, actor_id, action_description, risk_level, risk_score, governance_action, detail, prior_hash):
    return "|".join([
        timestamp, actor_id, action_description, risk_level, repr(risk_score),
        governance_action, detail or "", prior_hash,
    ])


def compute_hash(canonical: str) -> str:
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def create_schema(conn):
    conn.execute("""
        CREATE TABLE AuditEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TimestampUtc TEXT NOT NULL,
            ActorId TEXT NOT NULL,
            ActionDescription TEXT NOT NULL,
            RiskLevel TEXT NOT NULL,
            RiskScore REAL NOT NULL,
            GovernanceAction TEXT NOT NULL,
            Detail TEXT,
            PriorHash TEXT NOT NULL,
            EntryHash TEXT NOT NULL
        )
    """)


def append(conn, actor_id, action_description, risk_level, risk_score, governance_action, detail):
    cur = conn.execute("SELECT EntryHash FROM AuditEntries ORDER BY Id DESC LIMIT 1")
    row = cur.fetchone()
    prior_hash = row[0] if row else GENESIS_HASH

    timestamp = f"2026-08-30T00:00:{append.counter:02d}.000000Z"
    append.counter += 1

    canonical = canonicalize(timestamp, actor_id, action_description, risk_level, risk_score, governance_action, detail, prior_hash)
    entry_hash = compute_hash(canonical)

    conn.execute(
        """INSERT INTO AuditEntries
           (TimestampUtc, ActorId, ActionDescription, RiskLevel, RiskScore, GovernanceAction, Detail, PriorHash, EntryHash)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
        (timestamp, actor_id, action_description, risk_level, risk_score, governance_action, detail, prior_hash, entry_hash),
    )
    conn.commit()
    return conn.execute("SELECT last_insert_rowid()").fetchone()[0]


append.counter = 0


def verify_chain(conn):
    """Returns (ok: bool, first_broken_id: int|None, reason: str|None)."""
    cur = conn.execute("""
        SELECT Id, TimestampUtc, ActorId, ActionDescription, RiskLevel, RiskScore,
               GovernanceAction, Detail, PriorHash, EntryHash
        FROM AuditEntries ORDER BY Id ASC
    """)
    expected_prior_hash = GENESIS_HASH
    for row in cur:
        (entry_id, timestamp, actor_id, action_description, risk_level, risk_score,
         governance_action, detail, stored_prior_hash, stored_entry_hash) = row

        if stored_prior_hash != expected_prior_hash:
            return False, entry_id, "broken link to previous row (PriorHash mismatch)"

        canonical = canonicalize(timestamp, actor_id, action_description, risk_level, risk_score,
                                  governance_action, detail, stored_prior_hash)
        if compute_hash(canonical) != stored_entry_hash:
            return False, entry_id, "row content does not match its stored EntryHash (row was altered)"

        expected_prior_hash = stored_entry_hash

    return True, None, None


def run():
    db_path = os.path.join(tempfile.gettempdir(), "audit_trail_tamper_check.db")
    if os.path.exists(db_path):
        os.remove(db_path)

    conn = sqlite3.connect(db_path)
    create_schema(conn)

    entries = [
        ("demo-agent-01", "Answer a balance inquiry", "AutoExecutable", 1.0, "Allow", None),
        ("demo-agent-01", "Explain historical fund performance", "AutoExecutable", 1.0, "AllowWithModification", "DISC-PERFORMANCE: appended disclaimer"),
        ("demo-agent-01", "Draft a response revealing account details", "AutoExecutable", 1.5, "AllowWithModification", "PII-ACCOUNT: 1 match redacted"),
        ("demo-agent-01", "Promise guaranteed investment returns", "AutoExecutable", 1.75, "Block", "TOPIC-GUARANTEE: guaranteed-return language detected"),
        ("demo-agent-01", "Autonomously adjust a systemic pricing parameter", "RequiresHumanApproval", 3.0, "RequireHumanApproval", None),
    ]
    ids = [append(conn, *entry) for entry in entries]
    print(f"Appended {len(ids)} entries to a fresh SQLite audit trail at {db_path}")

    ok, broken_id, reason = verify_chain(conn)
    print(f"\nVerification on untampered trail: {'PASSED' if ok else 'FAILED'}")
    assert ok, "Untampered chain should verify cleanly"

    # Now simulate tampering: an operator directly UPDATEs a historical
    # row via raw SQL, completely bypassing the append() function (and
    # therefore the hash chain's own bookkeeping) -- exactly the scenario
    # a hash chain exists to make detectable.
    tampered_id = ids[2]  # "Draft a response revealing account details"
    print(f"\nSimulating tampering: directly UPDATEing row Id={tampered_id} "
          f"(changing ActionDescription, as an attacker hiding what an agent actually did might)...")
    conn.execute("UPDATE AuditEntries SET ActionDescription = ? WHERE Id = ?",
                 ("Answered a routine question", tampered_id))
    conn.commit()

    ok, broken_id, reason = verify_chain(conn)
    print(f"\nVerification after tampering: {'PASSED' if ok else 'FAILED'}")
    print(f"First broken entry: Id={broken_id} ({reason})")

    assert not ok, "Tampered chain must fail verification"
    assert broken_id == tampered_id, f"Expected verification to catch the tampered row itself (Id={tampered_id}), got Id={broken_id}"

    print(f"\nASSERTIONS PASSED: tampering with row {tampered_id} was detected, and the reported")
    print(f"broken entry is the exact row that was altered -- not merely 'something in the trail is wrong'.")

    conn.close()
    os.remove(db_path)


if __name__ == "__main__":
    run()
