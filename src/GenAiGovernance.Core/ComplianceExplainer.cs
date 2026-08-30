using System.Text.RegularExpressions;

namespace GenAiGovernance.Core;

/// <summary>
/// Retrieve-then-explain over <see cref="RegulatoryCorpus"/>: given a
/// question about why an action was gated (e.g. "why was this credit
/// decision blocked"), returns the most relevant regulatory summaries by
/// plain TF-IDF cosine similarity, with their source citations attached.
///
/// This is deliberately the least "AI" component in the toolkit -- no
/// embeddings, no LLM call, classic 1970s-vintage information retrieval --
/// which is the point the field guide's Section 05 makes about regulatory
/// explanation specifically: an explanation of *which rule applies* is a
/// retrieval problem with a small, curated, versioned corpus, not a
/// generation problem. Asking an LLM to explain regulatory grounding from
/// its own training data risks a fluent, wrong citation; grounding the
/// explanation in retrieval from a corpus the institution controls and can
/// audit is the safer shape, even if the retrieval itself is unglamorous.
///
/// TF-IDF is implemented by hand here (no external ML package) so this
/// class has zero dependencies beyond the .NET base class library. See
/// <c>verification/compliance_explainer_oracle.py</c> for a cross-check
/// against scikit-learn's <c>TfidfVectorizer</c> + cosine similarity on
/// the exact same corpus and query set -- run for real in this sandbox,
/// confirming this hand-rolled implementation's top-1 retrieval agrees
/// with the reference library's on every query in the test set.
/// </summary>
public sealed class ComplianceExplainer
{
    private static readonly Regex Tokenizer = new(@"[a-z0-9]+", RegexOptions.Compiled);

    private readonly IReadOnlyList<RegulatoryDocument> _corpus;
    private readonly List<Dictionary<string, double>> _docVectors;
    private readonly Dictionary<string, double> _idf;

    public ComplianceExplainer(IReadOnlyList<RegulatoryDocument> corpus)
    {
        _corpus = corpus;
        var tokenizedDocs = corpus.Select(d => Tokenize(d.Text)).ToList();

        int n = tokenizedDocs.Count;
        var docFrequency = new Dictionary<string, int>();
        foreach (var tokens in tokenizedDocs)
        {
            foreach (var term in tokens.Distinct())
            {
                docFrequency[term] = docFrequency.GetValueOrDefault(term) + 1;
            }
        }

        // Smoothed IDF (as scikit-learn's default does): log((1+n)/(1+df)) + 1.
        // Matching scikit-learn's exact formula, not just "a" TF-IDF
        // formula, is what makes the Python cross-check in
        // compliance_explainer_oracle.py a meaningful agreement rather
        // than two different-by-construction scores that coincidentally
        // rank the same document first.
        _idf = docFrequency.ToDictionary(kv => kv.Key, kv => Math.Log((1.0 + n) / (1.0 + kv.Value)) + 1.0);

        _docVectors = tokenizedDocs.Select(tokens => TfIdfVector(tokens, _idf)).ToList();
    }

    private static List<string> Tokenize(string text) =>
        Tokenizer.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();

    private static Dictionary<string, double> TfIdfVector(List<string> tokens, Dictionary<string, double> idf)
    {
        var termFrequency = new Dictionary<string, double>();
        foreach (var term in tokens)
        {
            termFrequency[term] = termFrequency.GetValueOrDefault(term) + 1.0;
        }

        var vector = new Dictionary<string, double>();
        foreach (var (term, tf) in termFrequency)
        {
            if (idf.TryGetValue(term, out double idfValue))
            {
                vector[term] = tf * idfValue;
            }
        }

        // L2-normalize -- scikit-learn's TfidfVectorizer normalizes by
        // default ('l2'), so this has to match for the cosine-similarity
        // cross-check to line up.
        double norm = Math.Sqrt(vector.Values.Sum(v => v * v));
        if (norm > 0)
        {
            foreach (var key in vector.Keys.ToList()) vector[key] /= norm;
        }
        return vector;
    }

    private static double CosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        double dot = 0;
        var (smaller, larger) = a.Count <= b.Count ? (a, b) : (b, a);
        foreach (var (term, weight) in smaller)
        {
            if (larger.TryGetValue(term, out double otherWeight))
            {
                dot += weight * otherWeight;
            }
        }
        return dot; // both vectors are already L2-normalized, so dot product == cosine similarity
    }

    public sealed record Explanation(RegulatoryDocument Document, double Similarity);

    /// <summary>
    /// Returns up to <paramref name="topK"/> corpus documents ranked by
    /// cosine similarity to <paramref name="query"/>, highest first,
    /// excluding zero-similarity matches (a query that shares no
    /// vocabulary with any document should return nothing, not the
    /// corpus's least-irrelevant entries).
    /// </summary>
    public IReadOnlyList<Explanation> Explain(string query, int topK = 3)
    {
        var queryVector = TfIdfVector(Tokenize(query), _idf);
        return _corpus
            .Select((doc, i) => new Explanation(doc, CosineSimilarity(queryVector, _docVectors[i])))
            .Where(e => e.Similarity > 0)
            .OrderByDescending(e => e.Similarity)
            .Take(topK)
            .ToList();
    }
}
