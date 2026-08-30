using SyntheticPDFs.Configuration;
using SyntheticPDFs.Git;
using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // The English vocabulary key: the tier 3 words the sheet uses, with definitions.
        // The model returns the words as data and the file is rendered here, so the
        // layout, the shuffle and the two-page repeat are all deterministic.
        private async Task<List<TexSourceModel>> GenerateVocabularyKey(SourceMetadata sm)
        {
            IReadOnlyList<SourceType> types = SourcePlan.TypesFor(sm.Archetype);

            SourceMetadata english = sm with { Rendition = SourceRendition.Original };

            String rootFilename = GetFilenameFromMetadata(english with { Type = SourceType.Root });

            String rootSource = RepoManager.GetContent(rootFilename).TexSource;

            // only the parts this archetype actually has - a poster has no workings, and
            // waiting for them would mean its key was never written
            String? worked = types.Contains(SourceType.WorkedSolutions)
                ? RepoManager.GetContent(
                    GetFilenameFromMetadata(english with { Type = SourceType.WorkedSolutions })).TexSource
                : null;

            String? answers = types.Contains(SourceType.Solutions)
                ? RepoManager.GetContent(
                    GetFilenameFromMetadata(english with { Type = SourceType.Solutions })).TexSource
                : null;

            List<VocabTerm> terms = await SourceGenerator.GenerateVocabularyTerms(
                rootSource, worked, answers, LLMService);

            // the shared definitions win over the model's, which is what makes them
            // standard across worksheets rather than merely suggested. matching is by
            // headword, so a sheet saying "numerators" still gets the agreed wording
            terms = L2VocabData.ApplyDictionary(terms, LoadDictionary());

            String tex = L2VocabKeyRenderer.Render(
                terms,
                new L2Macros.SourceMetadataTitle(sm.RootName, sm.Type, sm.Rendition),
                L2Settings.Colours,
                language: null,
                builtFrom: rootFilename,
                vocabularyKey: null);

            return new List<TexSourceModel>
            {
                new TexSourceModel
                {
                    FileNameFullPath = GetFilenameFromMetadata(sm),
                    TexSource = tex,
                }
            };
        }

        // The same key in another language: the English word, the word a mathematics
        // teacher in that language would use, and the definition translated. The
        // match-up that follows tests the translation rather than the meaning.
        private async Task<List<TexSourceModel>> GenerateTranslatedKey(SourceMetadata sm)
        {
            LanguageProfile? language = Languages.Get(sm.Language);

            if (language is null)
            {
                throw new ArgumentException(
                    $"'{sm.Language.Code}' has no usable entry in {L2Options.SectionName}:Languages");
            }

            SourceMetadata vocabMetadata = sm with
            {
                Language  = ISO639_3Code.eng,
                Type      = SourceType.Root,
                Rendition = SourceRendition.VocabKey,
            };

            String vocabFilename = GetFilenameFromMetadata(vocabMetadata);

            // the terms come out of the English key's own data block rather than being
            // read back off its table, so nothing depends on parsing generated LaTeX
            List<VocabTerm>? terms = L2VocabData.ReadBlock(
                RepoManager.GetContent(vocabFilename).TexSource);

            if (terms is null)
            {
                throw new Exception(
                    $"{vocabFilename} carries no vocabulary data block to translate");
            }

            List<VocabTerm> translated = await SourceGenerator.TranslateVocabularyTerms(
                terms, language, LLMService);

            String tex = L2VocabKeyRenderer.Render(
                translated,
                new L2Macros.SourceMetadataTitle(sm.RootName, sm.Type, sm.Rendition),
                L2Settings.Colours,
                language,
                builtFrom: GetFilenameFromMetadata(sm with
                {
                    Language = ISO639_3Code.eng, Rendition = SourceRendition.Original
                }),
                vocabularyKey: vocabFilename);

            return new List<TexSourceModel>
            {
                new TexSourceModel
                {
                    FileNameFullPath = GetFilenameFromMetadata(sm),
                    TexSource = tex,
                }
            };
        }
    }
}
