using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Rendering
{
    // The shared definitions, read from a file in the content repository rather than
    // from settings.
    //
    // Keeping them in the repository means a wording that reads badly to a teacher can
    // be changed the same way anything else in that repository is changed, by a commit
    // that can be discussed - and the same file compiles to a dictionary that is worth
    // having on its own. The trade is that the generator has to parse it, which is why
    // the entries are one predictable macro per line rather than free prose.
    //
    //   \dictentry{numerator}{the number above the line in a fraction}
    //   \dictentry[vertices]{vertex}{a corner where two or more edges meet}
    //
    // The optional argument lists extra forms of the word, for anything the rules in
    // WordForms will not reach.
    internal class MathsDictionary
    {
        internal const String EntryMacro = @"\dictentry";

        // headword -> definition, in the order the file lists them
        private readonly Dictionary<String, String> _byHeadword = new(StringComparer.Ordinal);

        // any form of a word -> the headword it belongs under
        private readonly Dictionary<String, String> _formsToHeadword = new(StringComparer.Ordinal);

        internal static MathsDictionary Empty => new();

        internal int Count => _byHeadword.Count;

        internal IReadOnlyDictionary<String, String> Entries => _byHeadword;

        #region Looking a word up

        // The definition for a word as it appeared in a sheet, or null if it is not one
        // we define. Tries the word itself, the forms the dictionary declared, then the
        // forms the rules suggest - and only ever accepts a candidate that is a headword.
        internal String? Define(String word)
        {
            String? headword = HeadwordFor(word);

            return headword is null ? null : _byHeadword[headword];
        }

        internal String? HeadwordFor(String word)
        {
            foreach (String candidate in WordForms.Candidates(word))
            {
                if (_formsToHeadword.TryGetValue(candidate, out String? headword))
                {
                    return headword;
                }
            }

            return null;
        }

        internal bool Defines(String word) => HeadwordFor(word) is not null;

        #endregion

        #region Reading the file

        internal static MathsDictionary Parse(String texSource, ILogger? logger = null)
        {
            MathsDictionary dictionary = new();

            int at = 0;

            String source = TexArguments.StripComments(texSource);

            while ((at = source.IndexOf(EntryMacro, at, StringComparison.Ordinal)) >= 0)
            {
                int cursor = at + EntryMacro.Length;

                // the macro name must end here, or \dictentryother would be read as ours
                if (cursor < source.Length && Char.IsLetter(source[cursor]))
                {
                    at = cursor;
                    continue;
                }

                // the file defines the macro before it uses it, and that occurrence has no
                // arguments of its own - it is the macro being named, not an entry
                if (!TexArguments.OpensAGroup(source, cursor))
                {
                    at = cursor;
                    continue;
                }

                List<String> variants = new();

                cursor = TexArguments.SkipSpace(source, cursor);

                if (cursor < source.Length && source[cursor] == '[')
                {
                    int close = source.IndexOf(']', cursor);

                    if (close < 0) { break; }

                    variants = source[(cursor + 1)..close]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .ToList();

                    cursor = TexArguments.SkipSpace(source, close + 1);
                }

                String? headword = TexArguments.ReadGroup(source, ref cursor);

                String? definition = TexArguments.ReadGroup(source, ref cursor);

                if (headword is null || definition is null)
                {
                    logger?.LogWarning(
                        "a {Macro} in the dictionary is missing its word or its definition, "
                        + "so it has been skipped", EntryMacro);

                    at = cursor > at ? cursor : at + EntryMacro.Length;
                    continue;
                }

                dictionary.Add(headword.Trim(), definition.Trim(), variants, logger);

                at = cursor;
            }

            return dictionary;
        }

        private void Add(String headword, String definition, List<String> variants, ILogger? logger)
        {
            String key = WordForms.Normalise(headword);

            if (key.Length == 0 || definition.Length == 0) { return; }

            if (_byHeadword.ContainsKey(key))
            {
                logger?.LogWarning(
                    "the dictionary defines '{Word}' more than once - the first definition is "
                    + "the one that will be used", headword);

                return;
            }

            _byHeadword[key] = definition;

            // the headword is a form of itself, and wins any collision
            _formsToHeadword[key] = key;

            foreach (String variant in variants)
            {
                String form = WordForms.Normalise(variant);

                if (form.Length == 0) { continue; }

                if (_formsToHeadword.TryGetValue(form, out String? existing) && existing != key)
                {
                    logger?.LogWarning(
                        "'{Form}' is listed as a form of both '{First}' and '{Second}' - keeping "
                        + "the first", variant, existing, headword);

                    continue;
                }

                _formsToHeadword[form] = key;
            }
        }

        #endregion
    }
}
