using System.Text;
using System.Text.Json;

namespace SyntheticPDFs.Logic
{
    // One tier 3 term. English is the word as it appears in the sheet; Translation is
    // empty in the English key and filled in for a translated one.
    internal record VocabTerm
    {
        internal required String English { get; init; }

        internal required String Definition { get; init; }

        internal String Translation { get; init; } = String.Empty;

        internal String TranslatedDefinition { get; init; } = String.Empty;
    }

    // The vocabulary a sheet uses, held as data rather than as a table of LaTeX.
    //
    // The model picks the words and defines them; everything after that - the layout,
    // the shuffle of the match-up, the repeat across pages - is done here, where it is
    // deterministic and testable. It also makes the list available to the prompts that
    // write the translated sheets, which need to know exactly which words to pick out.
    //
    // The data is carried inside the .tex as a comment block, so the file stays the only
    // artefact and nothing else in the pipeline needs to learn about a second one.
    internal static class L2VocabData
    {
        private const String OpenMarker  = "% ==== VOCABULARY DATA - generated, do not edit ====";
        private const String CloseMarker = "% ==== END VOCABULARY DATA ====";

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        // the wire shape, kept separate from VocabTerm so the property names the model
        // is asked for are short and stable
        private record TermDto(string? en, string? def, string? tr, string? trdef);

        private record TermsDto(List<TermDto>? terms);

        #region Reading what the model returned

        // Models wrap JSON in prose or code fences however they are asked not to, so
        // take the outermost braces rather than demanding the whole response parse.
        // Returns null when there is nothing usable, which the caller retries.
        internal static List<VocabTerm>? TryParse(String response)
        {
            if (String.IsNullOrWhiteSpace(response)) { return null; }

            int open = response.IndexOf('{');
            int close = response.LastIndexOf('}');

            if (open < 0 || close <= open) { return null; }

            String json = response[open..(close + 1)];

            TermsDto? parsed;

            try
            {
                parsed = JsonSerializer.Deserialize<TermsDto>(json, Json);
            }
            catch (JsonException)
            {
                return null;
            }

            if (parsed?.terms is null) { return null; }

            List<VocabTerm> terms = parsed.terms
                .Where(t => !String.IsNullOrWhiteSpace(t.en) && !String.IsNullOrWhiteSpace(t.def))
                .Select(t => new VocabTerm
                {
                    English              = t.en!.Trim(),
                    Definition           = t.def!.Trim(),
                    Translation          = (t.tr ?? String.Empty).Trim(),
                    TranslatedDefinition = (t.trdef ?? String.Empty).Trim(),
                })
                .ToList();

            // a key with no words in it is not a key
            return terms.Count == 0 ? null : Deduplicate(terms);
        }

        // the same word twice would give the match-up two identical rows, which has no
        // answer. first definition wins, since order is the order of the sheet
        private static List<VocabTerm> Deduplicate(List<VocabTerm> terms)
        {
            HashSet<String> seen = new(StringComparer.OrdinalIgnoreCase);

            return terms.Where(t => seen.Add(t.English)).ToList();
        }

        #endregion

        #region Carrying it inside the .tex

        internal static String Block(IReadOnlyList<VocabTerm> terms)
        {
            var dto = new TermsDto(terms
                .Select(t => new TermDto(t.English, t.Definition,
                    Blank(t.Translation), Blank(t.TranslatedDefinition)))
                .ToList());

            String json = JsonSerializer.Serialize(dto);

            StringBuilder sb = new();

            sb.AppendLine(OpenMarker);

            // one term per line, so a diff on this file is readable
            foreach (String line in Split(json))
            {
                sb.Append("% ").AppendLine(line);
            }

            sb.Append(CloseMarker);

            return sb.ToString();
        }

        private static String? Blank(String value) =>
            String.IsNullOrEmpty(value) ? null : value;

        // JSON has no line breaks of its own, and a single enormous comment line is
        // unreadable in a diff, so break it between terms
        private static IEnumerable<String> Split(String json) =>
            json.Replace("},{", "},\n{").Split('\n');

        internal static List<VocabTerm>? ReadBlock(String texSource)
        {
            String normalised = texSource.Replace("\r\n", "\n").Replace('\r', '\n');

            int open = normalised.IndexOf(OpenMarker, StringComparison.Ordinal);

            if (open < 0) { return null; }

            int close = normalised.IndexOf(CloseMarker, open, StringComparison.Ordinal);

            if (close < 0) { return null; }

            String body = normalised[(open + OpenMarker.Length)..close];

            // strip the comment character each line was written with
            String json = String.Join(String.Empty, body
                .Split('\n')
                .Select(l => l.TrimStart())
                .Where(l => l.StartsWith('%'))
                .Select(l => l[1..].Trim()));

            return TryParse(json);
        }

        #endregion

        #region The shared dictionary

        // Definitions held in the content repository so that the same word is explained
        // the same way on every sheet. The model's own definition is replaced wherever
        // one is shared, which is what makes them standard rather than merely suggested.
        //
        // Matching is by headword, so a sheet that says "numerators" or "Numerator" gets
        // the definition filed under "numerator".
        internal static List<VocabTerm> ApplyDictionary(
            IReadOnlyList<VocabTerm> terms, MathsDictionary dictionary)
        {
            return terms
                .Select(t => dictionary.Define(t.English) is String shared
                    ? t with { Definition = shared }
                    : t)
                .ToList();
        }

        // Whether a key already in the repository still agrees with the shared
        // definitions. This is what makes editing the dictionary rebuild the keys that
        // use the edited word, and only those - a key whose words are untouched by an
        // edit is left alone, however large the edit was.
        internal static bool MatchesDictionary(String texSource, MathsDictionary dictionary)
        {
            List<VocabTerm>? terms = ReadBlock(texSource);

            // no data block means we cannot show it is current, so it is not
            if (terms is null) { return false; }

            return terms.All(t =>
                dictionary.Define(t.English) is not String shared
                || String.Equals(shared, t.Definition, StringComparison.Ordinal));
        }

        #endregion

        #region The match-up

        // The definitions in an order that is not the order of the key, so that matching
        // them is actually a task. Seeded from the sheet's name rather than the clock,
        // so regenerating a key produces the same file and does not show up as a change.
        //
        // Rejects the identity ordering rather than trusting the shuffle: with four
        // terms it comes up one time in 24, and a match-up already in the right order
        // gives the answer away.
        internal static List<VocabTerm> Shuffled(IReadOnlyList<VocabTerm> terms, String seed)
        {
            if (terms.Count < 2) { return terms.ToList(); }

            Random random = new(StableHash(seed));

            List<VocabTerm> shuffled = terms.ToList();

            for (int attempt = 0; attempt < 8; attempt++)
            {
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }

                if (!shuffled.SequenceEqual(terms)) { return shuffled; }
            }

            // vanishingly unlikely, but a rotation is guaranteed to differ
            return terms.Skip(1).Concat(terms.Take(1)).ToList();
        }

        // string.GetHashCode is randomised per process, which would give a different
        // shuffle on every run and make every regeneration look like a change
        private static int StableHash(String seed)
        {
            unchecked
            {
                int hash = 17;

                foreach (char c in seed) { hash = hash * 31 + c; }

                return hash;
            }
        }

        #endregion
    }
}
