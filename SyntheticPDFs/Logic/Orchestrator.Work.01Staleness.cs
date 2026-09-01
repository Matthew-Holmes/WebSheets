using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Logic
{
    // What is no longer trustworthy, for a reason age alone cannot show.
    //
    // Ordering is decided by the content model: a file older than what it was derived
    // from is stale, and that is arithmetic on commit ages. What is decided here is the
    // other kind - rewording a definition does not touch the sheet, and changing a colour
    // touches nothing in the repository at all - and both are read out of what the file
    // itself records, so an edit rebuilds exactly what it affects.
    public partial class Orchestrator
    {
        // The dictionaries as the last pass found them, kept so that /ping can say a
        // dictionary has been broken. A problem here means somebody has pushed an edit
        // the parser cannot read, and the sooner they hear about it the better - a
        // dictionary that will not parse is one that quietly stops being applied.
        private IReadOnlyList<DictionaryProblem> _dictionaryProblems =
            Array.Empty<DictionaryProblem>();

        internal IReadOnlyList<String> DictionaryProblems =>
            _dictionaryProblems.Select(p => p.ToString()).ToList();

        // Words a vocabulary key uses that the shared dictionary has no definition for,
        // gathered as the keys are read below and emptied at the start of every pass.
        //
        // It rides along with the staleness check rather than making a sweep of its own
        // because that check already opens every key, and reading them all twice to ask
        // two questions of the same text would double the work for nothing.
        private readonly Dictionary<String, NewWord> _newWords = new(StringComparer.Ordinal);

        // the definition, and the sheet it was met on - the sheet only so that a word
        // met twice is settled the same way whatever order the repository is walked in
        private readonly record struct NewWord(String Definition, String Root);

        // English keys that are out of step but can be put back into step from what they
        // already carry, gathered as they are judged and emptied at the start of a pass.
        private readonly HashSet<(String Root, ContentKey Key)> _restating = new();

        // Read fresh each pass, since they live in the content repository and may have
        // been changed by anyone. A repository without a dictionary is not an error - it
        // simply has no shared definitions yet, and the model's own wording stands.
        private ContentModel WithDictionaries(ContentModel model)
        {
            List<DictionaryProblem> problems = new();

            Dictionary<String, DictionaryState> read = new(StringComparer.Ordinal);

            foreach (var (rootName, state) in model.Dictionaries)
            {
                read[rootName] = ReadDictionary(state, problems);
            }

            _dictionaryProblems = problems;

            foreach (DictionaryProblem problem in problems)
            {
                _logger.LogError("dictionary problem - {Problem}", problem);
            }

            return model with { Dictionaries = read };
        }

        private DictionaryState ReadDictionary(
            DictionaryState state, List<DictionaryProblem> problems)
        {
            if (state.English is null) { return state; }

            MathsDictionary definitions = MathsDictionary.Empty;

            String? english = TryRead(state.English.FullPath);

            if (english is null)
            {
                problems.Add(new DictionaryProblem(
                    state.English.FullPath, "the file could not be read"));
            }
            else
            {
                definitions = MathsDictionary.Parse(english, _logger);

                _logger.LogInformation(
                    "read {Count} shared definition(s) from {Path}",
                    definitions.Count, state.English.FullPath);
            }

            Dictionary<ISO639_3Code, L2Dictionary> translated = new();

            HashSet<ISO639_3Code> old = new();

            foreach (var (language, file) in state.Translations)
            {
                String? source = TryRead(file.FullPath);

                if (source is null)
                {
                    problems.Add(new DictionaryProblem(file.FullPath, "the file could not be read"));
                    continue;
                }

                translated[language] = L2Dictionary.Parse(
                    source, language, file.FullPath, problems);

                if (!L2Macros.MatchesSettings(
                        source, L2Settings.Colours, fallbackFont: L2Settings.FallbackFont))
                {
                    old.Add(language);
                }
            }

            return state with
            {
                Definitions          = definitions,
                Translated           = translated,
                BuiltFromOldSettings = old,
            };
        }

        private String? TryRead(String path)
        {
            try
            {
                return RepoManager.GetContent(path).TexSource;
            }
            catch (Exception e)
            {
                // one unreadable file must not stop the pass
                _logger.LogWarning("could not read {File}: {Message}", path, e.Message);

                return null;
            }
        }

        // What a vocabulary key knows that the shared dictionary does not.
        //
        // A key is written by a model, which defines every tier 3 word it picks out
        // whether or not the repository has an agreed wording for it. Those definitions
        // would otherwise stay on the one sheet that happened to prompt them, where
        // nobody would think to look and nothing else could reuse them. Collected here,
        // they are added to the dictionary and become the repository's own - editable in
        // one place, and translated once instead of once per sheet.
        private void NoteWordsTheDictionaryHasNot(
            String contents, DictionaryState dictionary, String rootName)
        {
            List<VocabTerm>? terms = L2VocabData.ReadBlock(contents);

            if (terms is null) { return; }

            foreach (VocabTerm term in terms)
            {
                if (dictionary.Definitions.Defines(term.English)) { continue; }

                String headword = WordForms.Normalise(term.English);

                if (headword.Length == 0 || term.Definition.Length == 0) { continue; }

                // the same word turns up on two sheets with two wordings often enough to
                // matter. the sheet that comes first alphabetically wins, so that what
                // the dictionary ends up saying does not depend on the order the
                // repository happened to be walked in
                if (_newWords.TryGetValue(headword, out NewWord seen))
                {
                    // Said once here so that the entries most likely to be wrong are the
                    // ones a person is pointed at. Two sheets wording a word differently
                    // is usually two ways of saying the same thing, but sometimes it is
                    // two different words - "leg" on a bearings sheet is a stretch of a
                    // journey and on a trigonometry sheet is a side of a triangle, and
                    // one shared definition cannot be right for both.
                    if (!String.Equals(seen.Definition, term.Definition, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "'{Word}' is defined differently on {First} and {Second}; the "
                            + "dictionary will take the wording from {Winner}",
                            headword, seen.Root, rootName,
                            String.CompareOrdinal(seen.Root, rootName) <= 0 ? seen.Root : rootName);
                    }

                    if (String.CompareOrdinal(seen.Root, rootName) <= 0) { continue; }
                }

                _newWords[headword] = new NewWord(term.Definition, rootName);
            }
        }

        // An English vocabulary key that is no longer in step with the repository around
        // it - because the shared dictionary now words one of its terms differently, or
        // because the colours or the layout have changed since.
        //
        // Neither is a reason to throw it away. The key carries the words it is made of
        // inside itself, so it can be rendered again from its own data with the
        // dictionary applied afresh, which costs nothing and is exactly what asking a
        // model again would have to produce. Rebuilding it instead would pay for the same
        // sheet's vocabulary a second time and get a different answer, because a model
        // asked twice picks a different set of words - and those new words would go into
        // the shared dictionary, put the next set of keys out of step, and start the
        // whole thing again. That is a loop, not a settling.
        //
        // A key with no data block is the one that cannot be restated - there is nothing
        // to restate it from - so that one is rebuilt, as anything unreadable is.
        private void JudgeGlossary(
            SheetState sheet,
            ContentFile file,
            String contents,
            DictionaryState dictionary,
            HashSet<ContentKey> outdated)
        {
            String? why = null;

            if (!L2VocabData.MatchesDictionary(contents, dictionary.Definitions))
            {
                why = "no longer agrees with the shared dictionary";
            }
            else if (!L2Macros.MatchesSettings(
                         contents, L2Settings.Colours, isKey: true, fallbackFont: null))
            {
                why = "was built from different settings";
            }

            if (why is null) { return; }

            if (L2VocabData.ReadBlock(contents) is null)
            {
                _logger.LogInformation(
                    "{File} {Why}, and carries no vocabulary data to rebuild it from",
                    file.FullPath, why);

                outdated.Add(file.Key);

                return;
            }

            _logger.LogInformation(
                "{File} {Why}, so it will be stated again from the words it already has",
                file.FullPath, why);

            _restating.Add((sheet.RootName, file.Key));
        }

        // Files that exist and are correctly ordered but were built from something that
        // has since changed. Only the derived ones are worth opening: an original is not
        // built from anything the generator records.
        private IReadOnlySet<ContentKey> Outdated(SheetState sheet, DictionaryState dictionary)
        {
            HashSet<ContentKey> outdated = new();

            foreach (ContentFile file in sheet.Files.Values)
            {
                SheetForm form = file.SourceMetadata.Form;

                if (form == SheetForm.Original) { continue; }

                String? contents = TryRead(file.FullPath);

                // leaving an unreadable file alone is the cheap answer: rebuilding it
                // would cost an API call on a guess
                if (contents is null) { continue; }

                // a variant is not translated and records no colours, so it is judged
                // against the rules that made it and nothing else
                if (form == SheetForm.RetrieveAndConnect)
                {
                    if (!RetrieveAndConnect.MatchesCurrentRules(contents))
                    {
                        _logger.LogInformation(
                            "{File} was retitled by older rules", file.FullPath);

                        outdated.Add(file.Key);
                    }

                    continue;
                }

                // An English key is judged and settled here rather than falling through to
                // the settings check below, because it is the one derived file that never
                // has to be thrown away: everything it was made of is written inside it.
                if (form == SheetForm.Glossary)
                {
                    NoteWordsTheDictionaryHasNot(contents, dictionary, sheet.RootName);

                    JudgeGlossary(sheet, file, contents, dictionary, outdated);

                    continue;
                }

                if (form == SheetForm.TranslatedGlossary
                    && !L2VocabData.MatchesTranslations(
                        contents, dictionary, file.SourceMetadata.Language))
                {
                    _logger.LogInformation(
                        "{File} no longer agrees with the dictionary in its own language",
                        file.FullPath);

                    outdated.Add(file.Key);
                    continue;
                }

                // the English key is settled above, so the only key left here is a
                // translated one
                bool isKey = form == SheetForm.TranslatedGlossary;

                // an English glossary borrows from nowhere, so it records no fallback and
                // is not judged against one
                String? fallback = file.SourceMetadata.Language == ISO639_3Code.eng
                    ? null
                    : L2Settings.FallbackFont;

                if (!L2Macros.MatchesSettings(contents, L2Settings.Colours, isKey, fallback))
                {
                    _logger.LogInformation(
                        "{File} was built from different settings", file.FullPath);

                    outdated.Add(file.Key);
                }
            }

            return outdated;
        }
    }
}
