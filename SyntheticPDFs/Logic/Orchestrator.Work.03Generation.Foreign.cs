using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

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

            if (sm.Form is not (SheetForm.ParallelText or SheetForm.Tier3Only))
            {
                throw new NotImplementedException($"no way to generate a {sm.Form}");
            }

            LanguageProfile? language = Languages.Get(sm.Language);

            if (language is null)
            {
                throw new ArgumentException(
                    $"'{sm.Language.Code}' has no usable entry in {L2Options.SectionName}:Languages");
            }

            // the English file this is a version of - the sheet itself, or its worked
            // solutions, or its answers
            String englishFilename = (sm with { Language = ISO639_3Code.eng, Form = SheetForm.Original, }).FilePath;

            String english = RepoManager.GetContent(englishFilename).TexSource;

            String keyFilename = (sm with { Part = SheetPart.Root, Form = SheetForm.TranslatedGlossary, }).FilePath;

            List<VocabTerm>? terms = L2VocabData.ReadBlock(
                RepoManager.GetContent(keyFilename).TexSource);

            if (terms is null)
            {
                throw new Exception($"{keyFilename} carries no vocabulary data block");
            }

            String body = await SourceGenerator.GenerateTranslatedBody(
                english, terms, language, L2Settings.Colours, sm.Form, LLMService);

            String tex = L2Document.Assemble(
                body,
                L2Document.DocumentClassOf(english),
                L2Document.PreambleOf(english),
                L2Macros.TitleFor(
                    new L2Macros.SourceMetadataTitle(sm.RootName, sm.Part, sm.Form), language),
                L2Settings.Colours,
                language,
                builtFrom: englishFilename,
                vocabularyKey: keyFilename,
                fallbackFont: L2Settings.FallbackFont);

            // last thing before it is committed, since a file that will not compile is
            // worse than one that is missing - the missing one comes back next pass
            if (L2Document.WhatIsMissingFrom(tex, english) is String missing)
            {
                throw new Exception($"the {sm.Form} of {sm.RootName} was not committed: {missing}");
            }

            return new List<TexSourceModel>
            {
                new TexSourceModel { FileNameFullPath = sm.FilePath, TexSource = tex }
            };
        }
    }
}
