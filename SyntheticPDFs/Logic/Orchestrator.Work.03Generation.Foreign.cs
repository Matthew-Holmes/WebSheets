using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // The two translated forms of a sheet: the whole text in parallel, or only the
        // tier 3 words glossed. Both are derived from the same two inputs - the English
        // file and the vocabulary key for that language - and differ only in the prompt.
        private async Task<List<TexSourceModel>> GenerateForeignLanguageSyntheticSource(
            GenerationRequest request)
        {
            SourceMetadata sm = request.Target;

            if (sm.Rendition is not (SourceRendition.ParallelText or SourceRendition.Tier3Only))
            {
                throw new NotImplementedException($"no way to generate a {sm.Rendition}");
            }

            LanguageProfile? language = Languages.Get(sm.Language);

            if (language is null)
            {
                throw new ArgumentException(
                    $"'{sm.Language.Code}' has no usable entry in {L2Options.SectionName}:Languages");
            }

            // the English file this is a version of - the sheet itself, or its worked
            // solutions, or its answers
            String englishFilename = GetFilenameFromMetadata(sm with
            {
                Language  = ISO639_3Code.eng,
                Rendition = SourceRendition.Original,
            });

            String english = RepoManager.GetContent(englishFilename).TexSource;

            String keyFilename = GetFilenameFromMetadata(sm with
            {
                Type      = SourceType.Root,
                Rendition = SourceRendition.L2Key,
            });

            List<VocabTerm>? terms = L2VocabData.ReadBlock(
                RepoManager.GetContent(keyFilename).TexSource);

            if (terms is null)
            {
                throw new Exception($"{keyFilename} carries no vocabulary data block");
            }

            String body = await SourceGenerator.GenerateTranslatedBody(
                english, terms, language, L2Settings.Colours, sm.Rendition, LLMService);

            String tex = L2Document.Assemble(
                body,
                L2Document.DocumentClassOf(english),
                L2Document.PackagesOf(english),
                L2Macros.TitleFor(
                    new L2Macros.SourceMetadataTitle(sm.RootName, sm.Type, sm.Rendition), language),
                L2Settings.Colours,
                language,
                builtFrom: englishFilename,
                vocabularyKey: keyFilename);

            return new List<TexSourceModel>
            {
                new TexSourceModel { FileNameFullPath = GetFilenameFromMetadata(sm), TexSource = tex }
            };
        }
    }
}
