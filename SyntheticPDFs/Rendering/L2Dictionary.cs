using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Rendering
{
    // One word in one language: the English headword it is filed under, the English
    // definition it was translated from, and the two translated strings.
    //
    // The English definition is carried alongside the translation on purpose. It is what
    // makes the file a cache rather than a snapshot: a word whose English wording has not
    // changed does not need translating again, and one whose wording has changed can be
    // spotted without asking a model anything.
    internal sealed record L2DictionaryEntry
    {
        // the English headword, normalised the same way the shared dictionary does
        internal required String Headword { get; init; }

        // the English definition this translation was made from
        internal required String English { get; init; }

        internal required String Word { get; init; }

        internal required String Definition { get; init; }

        internal bool StillMatches(String englishDefinition) =>
            String.Equals(English, englishDefinition, StringComparison.Ordinal);
    }

    // Something wrong with a dictionary file that a person has to fix. Kept rather than
    // thrown, because one bad line must not take a pass down, and because these are
    // reported back out through /ping so that a push that breaks a dictionary is noticed
    // by whoever pushed it rather than months later.
    internal sealed record DictionaryProblem(String File, String Message)
    {
        public override String ToString() => $"{File}: {Message}";
    }

    // The shared dictionary in one language.
    //
    // Written by the pipeline and read back by it on every pass. Reading it back is the
    // whole point: a word translated once stays translated, however many sheets go on to
    // use it, and a glossary in that language is then assembled from entries that are
    // already here rather than from an API call.
    //
    //   \dictentrytr{numerator}{the number above the line in a fraction}%
    //               {licznik}{liczba nad kreska ulamka}
    //
    // Four arguments rather than two, and in that order, so that the file reads as a
    // parallel text and so that the English definition each translation was made from
    // travels with it.
    internal sealed class L2Dictionary
    {
        internal const String EntryMacro = @"\dictentrytr";

        private readonly Dictionary<String, L2DictionaryEntry> _byHeadword =
            new(StringComparer.Ordinal);

        internal L2Dictionary(ISO639_3Code language)
        {
            Language = language;
        }

        internal ISO639_3Code Language { get; }

        internal int Count => _byHeadword.Count;

        internal IReadOnlyCollection<L2DictionaryEntry> Entries => _byHeadword.Values;

        internal static L2Dictionary Empty(ISO639_3Code language) => new(language);

        #region Looking a word up

        // by headword, as the shared dictionary files it - so a sheet that says
        // "numerators" reaches the same entry as one that says "numerator"
        internal L2DictionaryEntry? Find(String headword)
        {
            String key = WordForms.Normalise(headword);

            return _byHeadword.TryGetValue(key, out L2DictionaryEntry? entry) ? entry : null;
        }

        // The translation to use for a word whose English definition is this one, or null
        // if there is nothing usable - either the word is not here, or its English wording
        // has changed since it was translated, which makes the translation out of date
        // rather than merely old.
        internal L2DictionaryEntry? Current(String headword, String englishDefinition)
        {
            L2DictionaryEntry? entry = Find(headword);

            return entry is not null && entry.StillMatches(englishDefinition) ? entry : null;
        }

        internal bool Has(String headword) => Find(headword) is not null;

        #endregion

        #region Reading the file

        // Never throws. A file somebody has half-edited has to be readable as far as it
        // goes, so that the entries that are still intact keep working and the broken
        // ones are named - which is what the problems list is for.
        internal static L2Dictionary Parse(
            String texSource,
            ISO639_3Code language,
            String file,
            List<DictionaryProblem> problems)
        {
            L2Dictionary dictionary = new(language);

            String source = TexArguments.StripComments(texSource);

            int at = 0;

            while ((at = source.IndexOf(EntryMacro, at, StringComparison.Ordinal)) >= 0)
            {
                int cursor = at + EntryMacro.Length;

                // the macro name must end here, or \dictentrytrx would be read as ours
                if (cursor < source.Length && Char.IsLetter(source[cursor]))
                {
                    at = cursor;
                    continue;
                }

                // The file defines the macro before it uses it, so the name occurs once
                // inside \newcommand{\dictentrytr}[4]{...} without any arguments of its
                // own. That is the macro being named rather than used, and reading it as
                // a broken entry would report every dictionary as broken.
                if (!TexArguments.OpensAGroup(source, cursor))
                {
                    at = cursor;
                    continue;
                }

                String? headword   = TexArguments.ReadGroup(source, ref cursor);
                String? english    = TexArguments.ReadGroup(source, ref cursor);
                String? word       = TexArguments.ReadGroup(source, ref cursor);
                String? definition = TexArguments.ReadGroup(source, ref cursor);

                if (headword is null || english is null || word is null || definition is null)
                {
                    problems.Add(new DictionaryProblem(file,
                        $"a {EntryMacro} does not have its four arguments - it needs the English "
                        + "word, the English definition, the translated word and the translated "
                        + "definition, each in its own braces"));

                    at = cursor > at ? cursor : at + EntryMacro.Length;
                    continue;
                }

                String key = WordForms.Normalise(headword.Trim());

                if (key.Length == 0 || word.Trim().Length == 0)
                {
                    problems.Add(new DictionaryProblem(file,
                        $"a {EntryMacro} has no word in it - both the English word and its "
                        + "translation have to say something"));

                    at = cursor;
                    continue;
                }

                if (dictionary._byHeadword.ContainsKey(key))
                {
                    problems.Add(new DictionaryProblem(file,
                        $"'{headword.Trim()}' is translated more than once - the first "
                        + "translation is the one that will be used"));

                    at = cursor;
                    continue;
                }

                // the file holds them escaped for LaTeX; everything above this line works
                // in the plain text they were escaped from
                dictionary._byHeadword[key] = new L2DictionaryEntry
                {
                    Headword   = key,
                    English    = TexArguments.Unescape(english.Trim()),
                    Word       = TexArguments.Unescape(word.Trim()),
                    Definition = TexArguments.Unescape(definition.Trim()),
                };

                at = cursor;
            }

            return dictionary;
        }

        #endregion

        #region Changing it

        // A dictionary with these entries put in, replacing any of the same headword.
        // Immutable in the sense that matters: nothing already committed is edited in
        // place, a new file is written from the old one plus the difference.
        internal L2Dictionary With(IEnumerable<L2DictionaryEntry> entries)
        {
            L2Dictionary updated = new(Language);

            foreach (var (key, entry) in _byHeadword) { updated._byHeadword[key] = entry; }

            foreach (L2DictionaryEntry entry in entries)
            {
                updated._byHeadword[entry.Headword] = entry;
            }

            return updated;
        }

        // Only the headwords the shared dictionary still defines. A word taken out of the
        // English dictionary has no definition to be a translation of, so keeping it would
        // leave an entry nothing can check.
        internal L2Dictionary Without(IEnumerable<String> headwords)
        {
            HashSet<String> doomed = headwords.Select(WordForms.Normalise).ToHashSet();

            L2Dictionary kept = new(Language);

            foreach (var (key, entry) in _byHeadword)
            {
                if (!doomed.Contains(key)) { kept._byHeadword[key] = entry; }
            }

            return kept;
        }

        #endregion
    }
}
