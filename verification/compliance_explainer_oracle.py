#!/usr/bin/env python3
"""
Cross-checks ComplianceExplainer.cs's hand-rolled TF-IDF + cosine
similarity against scikit-learn's TfidfVectorizer, on the exact same
corpus (data/regulatory_corpus.json) and the exact same query set used in
ComplianceExplainerTests.cs. Two independent implementations of "retrieve
the most relevant regulatory summary" agreeing on top-1 for every query is
a real, actually-executed cross-check -- not the "reviewed but not run"
disclosure the rest of this portfolio uses for uncompiled C#, because the
retrieval MATH here is fully re-implemented and run in Python, both by
hand (mirroring the C# field-for-field) and via scikit-learn as an
independent reference.

Requires scikit-learn (available in this sandbox; not a runtime
dependency of the actual C# toolkit, which has zero third-party
dependencies beyond Microsoft.Data.Sqlite -- see ComplianceExplainer.cs's
doc comment for why that's a deliberate design choice, not an oversight).
"""
import json
import math
import os
import re

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import cosine_similarity

CORPUS_PATH = os.path.join(os.path.dirname(__file__), "..", "data", "regulatory_corpus.json")

TOKENIZER = re.compile(r"[a-z0-9]+")

QUERIES = [
    ("Why is our credit scoring model considered high-risk?", "EU-AIA-HR-01"),
    ("Can we decline a loan application based on national origin?", "FAIR-LENDING-01"),
    ("What automatic event logging and traceability obligations apply over the AI system's lifecycle?", "EU-AIA-HR-02"),
    ("What does NIST say about human override of AI systems?", "NIST-RMF-02"),
    ("Is it a conflict of interest to let AI recommend investments that benefit the firm?", "SEC-AI-COI-01"),
    ("Does the customer have a right to a human review of an automated credit denial?", "GDPR-AUTOMATED-01"),
]


def load_corpus():
    with open(CORPUS_PATH) as f:
        return json.load(f)


def tokenize(text: str) -> list[str]:
    return TOKENIZER.findall(text.lower())


def hand_rolled_tfidf(corpus_texts: list[str]):
    """Field-for-field mirror of ComplianceExplainer.cs: smoothed IDF
    (log((1+n)/(1+df)) + 1), raw term frequency, L2-normalized vectors,
    cosine similarity via dot product of normalized vectors."""
    tokenized = [tokenize(t) for t in corpus_texts]
    n = len(tokenized)

    df: dict[str, int] = {}
    for tokens in tokenized:
        for term in set(tokens):
            df[term] = df.get(term, 0) + 1
    idf = {term: math.log((1 + n) / (1 + d)) + 1.0 for term, d in df.items()}

    def vectorize(tokens: list[str]) -> dict[str, float]:
        tf: dict[str, float] = {}
        for term in tokens:
            tf[term] = tf.get(term, 0.0) + 1.0
        vec = {term: freq * idf[term] for term, freq in tf.items() if term in idf}
        norm = math.sqrt(sum(v * v for v in vec.values()))
        if norm > 0:
            vec = {k: v / norm for k, v in vec.items()}
        return vec

    doc_vectors = [vectorize(tokens) for tokens in tokenized]
    return idf, doc_vectors, vectorize


def cosine(a: dict[str, float], b: dict[str, float]) -> float:
    smaller, larger = (a, b) if len(a) <= len(b) else (b, a)
    return sum(w * larger[t] for t, w in smaller.items() if t in larger)


def run():
    corpus = load_corpus()
    texts = [doc["text"] for doc in corpus]
    ids = [doc["id"] for doc in corpus]

    idf, doc_vectors, vectorize = hand_rolled_tfidf(texts)

    sklearn_vectorizer = TfidfVectorizer(token_pattern=r"[a-z0-9]+", lowercase=True)
    sklearn_matrix = sklearn_vectorizer.fit_transform(texts)

    print(f"Corpus: {len(corpus)} documents. Vocabulary size (hand-rolled): {len(idf)}, "
          f"(scikit-learn): {len(sklearn_vectorizer.vocabulary_)}")
    print()

    agreements = 0
    for query, expected_id in QUERIES:
        # Hand-rolled (mirrors ComplianceExplainer.cs)
        query_vec = vectorize(tokenize(query))
        hand_scores = [(ids[i], cosine(query_vec, doc_vectors[i])) for i in range(len(corpus))]
        hand_top = max(hand_scores, key=lambda x: x[1])

        # scikit-learn reference
        query_matrix = sklearn_vectorizer.transform([query])
        sim = cosine_similarity(query_matrix, sklearn_matrix)[0]
        sklearn_top_idx = sim.argmax()
        sklearn_top = (ids[sklearn_top_idx], sim[sklearn_top_idx])

        agree = hand_top[0] == sklearn_top[0]
        agreements += agree
        matches_expected = hand_top[0] == expected_id

        print(f"Query: {query!r}")
        print(f"  hand-rolled top-1:   {hand_top[0]} (score {hand_top[1]:.4f})")
        print(f"  scikit-learn top-1:  {sklearn_top[0]} (score {sklearn_top[1]:.4f})")
        print(f"  expected (by design): {expected_id}")
        print(f"  {'AGREE' if agree else 'DISAGREE'}, {'matches expected' if matches_expected else 'DOES NOT MATCH EXPECTED'}")
        print()

        assert agree, f"Hand-rolled and scikit-learn disagree on top-1 for: {query!r}"
        assert matches_expected, f"Top-1 result does not match the expected document for: {query!r}"

    print(f"All {len(QUERIES)} queries: hand-rolled implementation agrees with scikit-learn's "
          f"TfidfVectorizer on top-1 retrieval, and both match the expected document.")
    print("This is the strongest confidence available without a .NET SDK that "
          "ComplianceExplainer.cs's formula (transcribed field-for-field into the "
          "hand-rolled function above) behaves as intended.")


if __name__ == "__main__":
    run()
