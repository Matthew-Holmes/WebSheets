using SyntheticPDFs.Models.Content;
using System.Text;
using System.Text.Json;

namespace SyntheticPDFs.Rendering
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

        // Whether a translated glossary already in the repository still agrees with the
        // dictionary in its language. This is what makes correcting a translation rebuild
        // the glossaries that use that word, and only those - and the rebuild costs
        // nothing, since the corrected wording is read straight out of the dictionary
        // rather than asked for again.
        internal static bool MatchesTranslations(
            String texSource, DictionaryState dictionary, ISO639_3Code language)
        {
            List<VocabTerm>? terms = ReadBlock(texSource);

            if (terms is null) { return false; }

            return terms.All(t =>
                dictionary.Lookup(language, t.English, t.Definition) is not L2DictionaryEntry entry
                || (String.Equals(entry.Word, t.Translation, StringComparison.Ordinal)
                    && String.Equals(
                        entry.Definition, t.TranslatedDefinition, StringComparison.Ordinal)));
        }

        #endregion

        #region The order the key is read in

        // A key is looked up by its English word, so that is what it is ordered by -
        // including the translated keys, which keep the English order rather than
        // sorting by the translation, so that the same sheet's keys read the same way
        // whichever language a pupil has.
        internal static List<VocabTerm> Alphabetical(IReadOnlyList<VocabTerm> terms) =>
            terms.OrderBy(t => t.English, StringComparer.OrdinalIgnoreCase).ToList();

        #endregion

        #region The match-up

        // How far a definition may sit from the word it belongs to.
        //
        // An unbounded shuffle puts a definition anywhere, which means an answer line
        // can run the full height of the page, and fifteen of those cross into
        // something nobody can read. Keeping every definition within five rows bounds
        // how steep a line can be, and the match-up is no easier for it - the pupil
        // still has to know which of eleven it is.
        internal const int MaxDisplacement = 5;

        // The definitions in an order that is not the order of the key, so that matching
        // them is actually a task. Seeded from the sheet's name rather than the clock,
        // so regenerating a key produces the same file and does not show up as a change.
        internal static List<VocabTerm> Shuffled(IReadOnlyList<VocabTerm> terms, String seed)
        {
            if (terms.Count < 2) { return terms.ToList(); }

            Random random = new(StableHash(seed));

            int[] order = BoundedShuffle(terms.Count, random);

            // A definition the repair could not move is possible, on an unlucky draw
            // near the end of a long list. Drawing again is cheaper than engineering
            // it away, and since the seed is the sheet's name, whatever comes out is
            // what that sheet gets every time.
            for (int attempt = 1; attempt < 8 && LeavesOneInPlace(order); attempt++)
            {
                order = BoundedShuffle(terms.Count, random);
            }

            return order.Select(i => terms[i]).ToList();
        }

        private static bool LeavesOneInPlace(int[] order) =>
            order.Where((definition, row) => definition == row).Any();

        // Fills the rows in order, each from the definitions still unplaced that are
        // near enough to it. Built rather than shuffled-and-checked because a random
        // permutation almost never satisfies the bound once there are more than a few
        // terms, so rejection would loop for a long time and then give up.
        //
        // Returns which definition each row holds, rather than the terms themselves,
        // because that is what the bound and the repair below are stated in terms of.
        private static int[] BoundedShuffle(int count, Random random)
        {
            List<int> unplaced = Enumerable.Range(0, count).ToList();

            int[] from = new int[count];

            for (int row = 0; row < count; row++)
            {
                from[row] = Take(unplaced, row, random);
            }

            Unfix(from);

            return from;
        }

        // unplaced stays ascending, so the definitions near enough to this row are a
        // prefix of it, and the one at the front is always the most urgent
        private static int Take(List<int> unplaced, int row, Random random)
        {
            int index = 0;

            // the front one has run out of rows it can still reach, so it goes here
            if (unplaced[0] + MaxDisplacement > row)
            {
                int reachable = 0;

                while (reachable < unplaced.Count
                    && unplaced[reachable] <= row + MaxDisplacement)
                {
                    reachable++;
                }

                index = random.Next(reachable);
            }

            int chosen = unplaced[index];

            unplaced.RemoveAt(index);

            return chosen;
        }

        // A definition left beside its own word is a free answer, and draws a line
        // straight across the answer page that says so. Swapping the two rows clears
        // it, so long as the other row's definition can afford the move - which is
        // why the nearest row that can is looked for rather than only the next one.
        private static void Unfix(int[] from)
        {
            for (int row = 0; row < from.Length; row++)
            {
                if (from[row] != row) { continue; }

                for (int step = 1; step <= MaxDisplacement; step++)
                {
                    if (TrySwap(from, row, row - step) || TrySwap(from, row, row + step))
                    {
                        break;
                    }
                }
            }
        }

        // the fixed definition moves to `other`, which it can reach because `other` is
        // within the bound of `row`; the definition at `other` has to reach `row`
        private static bool TrySwap(int[] from, int row, int other)
        {
            if (other < 0 || other >= from.Length) { return false; }

            if (!Fits(from[other], row)) { return false; }

            (from[row], from[other]) = (from[other], from[row]);

            return true;
        }

        private static bool Fits(int definition, int row) =>
            Math.Abs(definition - row) <= MaxDisplacement;

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
