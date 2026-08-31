using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // How many words are translated in one pass.
        //
        // A first run against a full dictionary would otherwise be one enormous prompt, and
        // a model asked for three hundred translations at once returns fewer than it was
        // given. Taking a bite at a time makes each request one the model can actually
        // answer, and the next pass picks up where this one stopped - the file itself is
        // the record of how far it has got.
        private const int WordsPerRefresh = 40;

        // Brings one language's dictionary back into step with the shared one.
        //
        // Only the difference is translated. A word whose English definition has not
        // changed keeps the translation it already has, so editing one definition costs one
        // word rather than a whole dictionary, and a word that has been taken out of the
        // shared dictionary takes its translation with it.
        private async Task<List<TexSourceModel>> RefreshDictionary(
            SourceMetadata target, ContentModel model)
        {
            if (!model.Dictionaries.TryGetValue(target.RootName, out DictionaryState? state)
                || state.English is null)
            {
                throw new Exception($"there is no shared dictionary at {target.RootName}");
            }

            LanguageProfile language = Profile(target.Language);

            L2Dictionary existing = state.In(target.Language)
                ?? L2Dictionary.Empty(target.Language);

            IReadOnlyList<KeyValuePair<String, String>> untranslated =
                Untranslated(state, existing);

            IReadOnlyList<String> abandoned = Abandoned(state, existing);

            List<L2DictionaryEntry> fresh = new();

            if (untranslated.Count > 0)
            {
                List<VocabTerm> wanted = untranslated
                    .Take(WordsPerRefresh)
                    .Select(e => new VocabTerm { English = e.Key, Definition = e.Value })
                    .ToList();

                _logger.LogInformation(
                    "translating {Taken} of {Total} outstanding dictionary word(s) into {Language}",
                    wanted.Count, untranslated.Count, language.TitleName);

                List<VocabTerm> translated = await SourceGenerator.TranslateVocabularyTerms(
                    wanted, language, LLMService);

                fresh = translated
                    .Select(t => new L2DictionaryEntry
                    {
                        Headword   = WordForms.Normalise(t.English),
                        English    = t.Definition,
                        Word       = t.Translation,
                        Definition = t.TranslatedDefinition,
                    })
                    .ToList();
            }

            L2Dictionary updated = existing.With(fresh).Without(abandoned);

            if (abandoned.Count > 0)
            {
                _logger.LogInformation(
                    "dropped {Count} {Language} translation(s) of words the shared dictionary no "
                    + "longer defines", abandoned.Count, language.TitleName);
            }

            String tex = L2DictionaryRenderer.Render(
                updated, language, L2Settings.Colours,
                builtFrom: state.English.FullPath,
                fallbackFont: L2Settings.FallbackFont);

            return new List<TexSourceModel>
            {
                new TexSourceModel { FileNameFullPath = target.FilePath, TexSource = tex },
            };
        }

        // Adds the words the sheets have met to the shared dictionary.
        //
        // Nothing is asked of a model here. Every word arrives with the definition the
        // vocabulary key that met it was already written with, so this is only a matter
        // of moving a definition from the one sheet that happens to carry it into the
        // file the whole repository reads - after which it can be reworded in one place,
        // it is translated once rather than once per sheet, and every key that uses it
        // is brought into line with the new wording.
        private Task<List<TexSourceModel>> ExtendDictionary(
            SourceMetadata target, ContentModel model)
        {
            if (!model.Dictionaries.TryGetValue(target.RootName, out DictionaryState? state)
                || state.English is null)
            {
                throw new Exception($"there is no shared dictionary at {target.RootName}");
            }

            IReadOnlyList<KeyValuePair<String, String>> adding = WordsToAdd(state);

            if (adding.Count == 0)
            {
                // something added them between the batch being chosen and now, which is
                // no reason to write the file again
                return Task.FromResult(new List<TexSourceModel>());
            }

            String existing = RepoManager.GetContent(state.English.FullPath).TexSource;

            _logger.LogInformation(
                "adding {Count} word(s) met on a worksheet to {Path}: {Words}",
                adding.Count, state.English.FullPath,
                String.Join(", ", adding.Select(e => e.Key)));

            return Task.FromResult(new List<TexSourceModel>
            {
                new TexSourceModel
                {
                    FileNameFullPath = state.English.FullPath,
                    TexSource        = MathsDictionaryWriter.Add(existing, adding),
                },
            });
        }

        private LanguageProfile Profile(ISO639_3Code language) =>
            Languages.Get(language)
            ?? throw new ArgumentException(
                $"'{language.Code}' has no usable entry in {L2Options.SectionName}:Languages");
    }
}
