using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Logic
{
    // Which translated dictionaries are out of step with the shared one.
    //
    // A dictionary is not planned the way a sheet is. A sheet that is out of date is
    // removed and made again; a dictionary that is out of date is brought back into step,
    // keeping every translation that is still current. So this decides refreshes rather
    // than creations, and the answer is read out of the parsed dictionaries themselves
    // rather than out of commit ages.
    public partial class Orchestrator
    {
        private IEnumerable<Candidate> DictionaryCandidates(ContentModel model, int rootOrder)
        {
            List<Candidate> candidates = new();

            foreach (DictionaryState state in model.Dictionaries.Values)
            {
                // there is nothing to translate until somebody has written the English one
                if (!state.Exists) { continue; }

                if (WordsToAdd(state).Count > 0)
                {
                    candidates.Add(new Candidate
                    {
                        Request   = DictionaryJob(
                            state.RootName, ISO639_3Code.eng,
                            SheetForm.Original, GenerationJob.ExtendDictionary),
                        Priority  = GenerationPriority.SharedDictionary,
                        RootOrder = rootOrder,
                        Sequence  = candidates.Count,
                    });
                }

                foreach (ISO639_3Code language in LanguagesWantingADictionary(model, state))
                {
                    if (!NeedsRefreshing(state, language)) { continue; }

                    candidates.Add(new Candidate
                    {
                        Request   = DictionaryJob(
                            state.RootName, language,
                            SheetForm.ParallelText, GenerationJob.RefreshDictionary),
                        Priority  = GenerationPriority.TranslatedDictionary,
                        RootOrder = rootOrder,
                        Sequence  = candidates.Count,
                    });
                }
            }

            return candidates;
        }

        private static GenerationRequest DictionaryJob(
            String rootName, ISO639_3Code language, SheetForm form, GenerationJob job) =>
            new GenerationRequest
            {
                Target = new SourceMetadata
                {
                    RootName  = rootName,
                    Archetype = SheetArchetypes.SharedDictionary,
                    Language  = language,
                    Part      = SheetPart.Root,
                    Form      = form,
                },
                Job = job,
            };

        // How many words are added to the shared dictionary in one pass.
        //
        // Not a cost - adding a word is string work and costs nothing - but a commit of
        // three hundred entries to a file somebody curates by hand is one nobody can
        // read. Translating them is capped at the same number a pass, so letting them in
        // faster than this would not get any of them onto a sheet any sooner.
        private const int WordsPerAddition = 40;

        // Words a vocabulary key defined that the shared dictionary does not, in the
        // order they will be written. Alphabetical rather than in the order they were
        // met, so that a pass adds a readable slice of the alphabet rather than a
        // scattering, and so that two runs over the same repository agree.
        internal IReadOnlyList<KeyValuePair<String, String>> WordsToAdd(DictionaryState state) =>
            _newWords
                .Where(w => !state.Definitions.Defines(w.Key))
                .OrderBy(w => w.Key, StringComparer.Ordinal)
                .Take(WordsPerAddition)
                .Select(w => new KeyValuePair<String, String>(w.Key, w.Value.Definition))
                .ToList();

        // A dictionary is worth having in a language the repository is actually producing
        // content in, and in every language that already has one.
        //
        // The second half is the important one, and is why this is not simply the eager
        // list: a dictionary already in the repository is kept in step whether or not its
        // language is eager, because it is a file somebody will read and because letting
        // it drift would leave every glossary built from it disagreeing with it.
        private IEnumerable<ISO639_3Code> LanguagesWantingADictionary(
            ContentModel model, DictionaryState state)
        {
            HashSet<ISO639_3Code> wanted = new(state.Translations.Keys);

            foreach (ISO639_3Code language in Languages.EagerLanguages) { wanted.Add(language); }

            foreach (ContentFile file in model.AllFiles)
            {
                if (file.SourceMetadata.Language != ISO639_3Code.eng)
                {
                    wanted.Add(file.SourceMetadata.Language);
                }
            }

            // one that cannot be typeset would fail the moment it was tried
            return wanted.Where(Languages.CanGenerate);
        }

        // Whether this language's dictionary says what the shared one now says. Three
        // things can put it out of step, and only the first of them costs anything:
        //
        //   a word has been added, or its English definition reworded, so there is
        //     nothing current to translate it with;
        //   a word has been taken out of the shared dictionary, so its translation has
        //     nothing left to be a translation of;
        //   the file was built from settings that have since changed.
        private static bool NeedsRefreshing(DictionaryState state, ISO639_3Code language)
        {
            L2Dictionary? translated = state.In(language);

            // no dictionary in this language yet, and something to put in it
            if (translated is null) { return state.Definitions.Count > 0; }

            if (state.BuiltFromOldSettings.Contains(language)) { return true; }

            if (Untranslated(state, translated).Count > 0) { return true; }

            return Abandoned(state, translated).Count > 0;
        }

        // Words the shared dictionary defines that this language has no current
        // translation for - either it has never had one, or the English wording has been
        // changed since and the translation is of something the sheet no longer says.
        internal static IReadOnlyList<KeyValuePair<String, String>> Untranslated(
            DictionaryState state, L2Dictionary translated) =>
            state.Definitions.Entries
                .Where(e => translated.Current(e.Key, e.Value) is null)
                .ToList();

        // Translations of words the shared dictionary no longer defines.
        internal static IReadOnlyList<String> Abandoned(
            DictionaryState state, L2Dictionary translated) =>
            translated.Entries
                .Select(e => e.Headword)
                .Where(headword => !state.Definitions.Entries.ContainsKey(headword))
                .ToList();
    }
}
