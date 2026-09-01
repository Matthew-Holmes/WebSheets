using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // The English glossary: the tier 3 words the sheet uses, with definitions.
        //
        // The model returns the words as data and the file is rendered here, so the
        // layout, the shuffle and the two-page repeat are all deterministic.
        private async Task<List<TexSourceModel>> GenerateGlossary(
            SourceMetadata sm, ContentModel model)
        {
            SourceMetadata english = sm with { Form = SheetForm.Original };

            String rootFilename = (english with { Part = SheetPart.Root }).FilePath;

            String rootSource = RepoManager.GetContent(rootFilename).TexSource;

            // only the parts this archetype actually has - a poster has no workings, and
            // waiting for them would mean its glossary was never written
            String? worked = sm.Archetype.HasWorkedSolutions
                ? RepoManager.GetContent(
                    (english with { Part = SheetPart.WorkedSolutions }).FilePath).TexSource
                : null;

            String? answers = sm.Archetype.HasSolutions
                ? RepoManager.GetContent(
                    (english with { Part = SheetPart.Solutions }).FilePath).TexSource
                : null;

            List<VocabTerm> terms = await SourceGenerator.GenerateVocabularyTerms(
                rootSource, worked, answers, LLMService);

            return new List<TexSourceModel> { RenderGlossary(sm, terms, model) };
        }

        // The same key again, from the words it already picked out.
        //
        // Nothing is asked of a model, in either language. A key that has fallen out of
        // step - because a dictionary now words one of its terms differently, or because
        // the colours or the layout have changed - has everything needed to bring it back
        // inside it, so this is the dictionary applied afresh and the file rendered
        // again. That is what makes editing a definition free across the whole
        // repository, however many sheets use the word and however many languages they
        // are printed in.
        private List<TexSourceModel> RestateGlossary(SourceMetadata sm, ContentModel model)
        {
            String contents = RepoManager.GetContent(sm.FilePath).TexSource;

            List<VocabTerm>? terms = L2VocabData.ReadBlock(contents);

            // only a key with a data block is ever asked for, so this is a broken
            // invariant rather than a file to guess at
            if (terms is null)
            {
                throw new Exception($"{sm.FilePath} carries no vocabulary data to state again");
            }

            _logger.LogInformation(
                "stating {File} again from its own {Count} word(s)", sm.FilePath, terms.Count);

            if (sm.Form == SheetForm.Glossary)
            {
                return new List<TexSourceModel> { RenderGlossary(sm, terms, model) };
            }

            LanguageProfile language = Profile(sm.Language);

            DictionaryState dictionary = model.DictionaryAt(ContentRepository.DictionaryPath);

            // The dictionary in this language answers what it can. A word it does not
            // cover keeps the translation the file already carries - a translation of the
            // same English wording, since the English key this was made from has not
            // moved. Buying it again would pay for a translation we already have.
            List<VocabTerm> restated = terms
                .Select(t =>
                    dictionary.Lookup(language.Code, t.English, t.Definition)
                        is L2DictionaryEntry entry
                        ? t with
                          {
                              Translation          = entry.Word,
                              TranslatedDefinition = entry.Definition,
                          }
                        : t)
                .ToList();

            return new List<TexSourceModel> { RenderTranslatedGlossary(sm, language, restated) };
        }

        // Written once and used by both, so that a key stated again is byte for byte the
        // key that would have been written from scratch - which is what lets the cheap
        // path be taken without wondering whether it produces something different.
        private TexSourceModel RenderGlossary(
            SourceMetadata sm, IReadOnlyList<VocabTerm> terms, ContentModel model)
        {
            String rootFilename =
                (sm with { Form = SheetForm.Original, Part = SheetPart.Root }).FilePath;

            // the shared definitions win over the model's, which is what makes them
            // standard across worksheets rather than merely suggested. matching is by
            // headword, so a sheet saying "numerators" still gets the agreed wording
            List<VocabTerm> shared = L2VocabData.ApplyDictionary(
                terms, model.DictionaryAt(ContentRepository.DictionaryPath).Definitions);

            String tex = L2VocabKeyRenderer.Render(
                shared,
                new L2Macros.SourceMetadataTitle(sm.RootName, sm.Part, sm.Form),
                L2Settings.Colours,
                language: null,
                builtFrom: rootFilename,
                vocabularyKey: null,
                fallbackFont: null);

            return new TexSourceModel { FileNameFullPath = sm.FilePath, TexSource = tex };
        }

        // The same glossary in another language: the English word, the word a mathematics
        // teacher in that language would use, and the definition translated. The match-up
        // that follows tests the translation rather than the meaning.
        //
        // Every word this sheet can take from the shared dictionary is taken from it, and
        // only what is left over is sent to a model. A repository whose dictionary covers
        // its vocabulary produces these for nothing at all - which is the point of keeping
        // the dictionary translated, and why the same word reads the same way on every
        // sheet a pupil is given.
        private async Task<List<TexSourceModel>> GenerateTranslatedGlossary(
            SourceMetadata sm, ContentModel model)
        {
            LanguageProfile language = Profile(sm.Language);

            SourceMetadata englishGlossary = sm with
            {
                Language = ISO639_3Code.eng,
                Part     = SheetPart.Root,
                Form     = SheetForm.Glossary,
            };

            String glossaryFilename = englishGlossary.FilePath;

            // the terms come out of the English glossary's own data block rather than
            // being read back off its table, so nothing depends on parsing generated LaTeX
            List<VocabTerm>? terms = L2VocabData.ReadBlock(
                RepoManager.GetContent(glossaryFilename).TexSource);

            if (terms is null)
            {
                throw new Exception(
                    $"{glossaryFilename} carries no vocabulary data block to translate");
            }

            List<VocabTerm> translated = await TranslateUsingDictionary(
                terms, model.DictionaryAt(ContentRepository.DictionaryPath), language);

            return new List<TexSourceModel>
            {
                RenderTranslatedGlossary(sm, language, translated),
            };
        }

        // As with the English pair, written once and used by both, so that a key stated
        // again is byte for byte the key that would have been written from scratch.
        private TexSourceModel RenderTranslatedGlossary(
            SourceMetadata sm, LanguageProfile language, IReadOnlyList<VocabTerm> terms)
        {
            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(sm.RootName, sm.Part, sm.Form),
                L2Settings.Colours,
                language,
                builtFrom: (sm with { Language = ISO639_3Code.eng, Form = SheetForm.Original }).FilePath,
                vocabularyKey: (sm with
                {
                    Language = ISO639_3Code.eng,
                    Part     = SheetPart.Root,
                    Form     = SheetForm.Glossary,
                }).FilePath,
                fallbackFont: L2Settings.FallbackFont);

            return new TexSourceModel { FileNameFullPath = sm.FilePath, TexSource = tex };
        }

        // The dictionary answers what it can, and the model is asked only about the rest.
        // Nothing is sent at all when the dictionary covers the whole sheet, which is the
        // usual case once a dictionary has settled.
        private async Task<List<VocabTerm>> TranslateUsingDictionary(
            IReadOnlyList<VocabTerm> terms, DictionaryState dictionary, LanguageProfile language)
        {
            Dictionary<String, VocabTerm> known = new(StringComparer.Ordinal);

            List<VocabTerm> missing = new();

            foreach (VocabTerm term in terms)
            {
                L2DictionaryEntry? entry =
                    dictionary.Lookup(language.Code, term.English, term.Definition);

                if (entry is null)
                {
                    missing.Add(term);
                    continue;
                }

                known[term.English] = term with
                {
                    Translation          = entry.Word,
                    TranslatedDefinition = entry.Definition,
                };
            }

            _logger.LogInformation(
                "{Known} of {Total} {Language} term(s) came from the shared dictionary",
                known.Count, terms.Count, language.TitleName);

            if (missing.Count > 0)
            {
                foreach (VocabTerm term in
                    await SourceGenerator.TranslateVocabularyTerms(missing, language, LLMService))
                {
                    known[term.English] = term;
                }
            }

            // the English glossary decides the order and the membership, so walk it rather
            // than whatever came back
            return terms.Select(t => known[t.English]).ToList();
        }
    }
}
