using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Rendering
{
    // Reduces a word as it appears in a sheet to the headword it belongs under, so that
    // "Numerators", "numerator" and "NUMERATOR" all find the same definition.
    //
    // The rules here are deliberately allowed to be wrong, because nothing acts on a
    // candidate unless it turns out to be a headword the dictionary actually defines.
    // That is what makes an aggressive rule safe: stripping "ing" from "ring" suggests
    // "r", which is not a headword, so nothing matches and the word is left alone. A
    // stemmer that had to be right on its own could not take that liberty.
    internal static class WordForms
    {
        // Plurals that no rule will get right. Mathematics has more of these than most
        // subjects, because so much of its vocabulary is Latin or Greek.
        private static readonly Dictionary<String, String> Irregular =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["axes"]        = "axis",
                ["bases"]       = "base",
                ["criteria"]    = "criterion",
                ["data"]        = "datum",
                ["dice"]        = "die",
                ["feet"]        = "foot",
                ["foci"]        = "focus",
                ["formulae"]    = "formula",
                ["formulas"]    = "formula",
                ["hypotheses"]  = "hypothesis",
                ["indices"]     = "index",
                ["loci"]        = "locus",
                ["matrices"]    = "matrix",
                ["maxima"]      = "maximum",
                ["media"]       = "medium",
                ["minima"]      = "minimum",
                ["parentheses"] = "parenthesis",
                ["polyhedra"]   = "polyhedron",
                ["radii"]       = "radius",
                ["vertices"]    = "vertex",
    };

        // Everything worth trying for one word, most literal first, so a headword that is
        // itself a plural - "axes" if someone chose to define it that way - wins over the
        // singular the rules would suggest.
        internal static IEnumerable<String> Candidates(String word)
        {
            String cleaned = Clean(word);

            if (cleaned.Length == 0) { yield break; }

            yield return cleaned;

            if (Irregular.TryGetValue(cleaned, out String? irregular))
            {
                yield return irregular;
            }

            foreach (String candidate in Regular(cleaned))
            {
                yield return candidate;
            }
        }

        private static IEnumerable<String> Regular(String word)
        {
            // identities -> identity, simplifies -> simplify
            if (word.EndsWith("ies", StringComparison.Ordinal) && word.Length > 4)
            {
                yield return word[..^3] + "y";
            }

            // simplified -> simplify, multiplied -> multiply
            if (word.EndsWith("ied", StringComparison.Ordinal) && word.Length > 4)
            {
                yield return word[..^3] + "y";
            }

            // boxes -> box, matches -> match
            if (word.EndsWith("es", StringComparison.Ordinal) && word.Length > 3)
            {
                yield return word[..^2];

                // squares -> square, where the e belongs to the word
                yield return word[..^1];
            }

            // fractions -> fraction. "ss" is not a plural ending, so mass stays mass
            if (word.EndsWith("s", StringComparison.Ordinal)
                && !word.EndsWith("ss", StringComparison.Ordinal)
                && word.Length > 2)
            {
                yield return word[..^1];
            }

            // rounding -> round, simplifying -> simplify
            if (word.EndsWith("ing", StringComparison.Ordinal) && word.Length > 4)
            {
                yield return word[..^3];

                // dividing -> divide
                yield return word[..^3] + "e";
            }

            // rounded -> round
            if (word.EndsWith("ed", StringComparison.Ordinal) && word.Length > 3)
            {
                yield return word[..^2];

                // divided -> divide
                yield return word[..^1];
            }
        }

        // punctuation the word may have picked up from the sentence it sat in, and the
        // case it happened to be written in
        private static String Clean(String word)
        {
            String trimmed = word.Trim().Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '\'');

            return trimmed.ToLowerInvariant();
        }

        internal static String Normalise(String word) => Clean(word);
    }
}
