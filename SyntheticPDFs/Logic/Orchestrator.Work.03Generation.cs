using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // One request can settle more than one file - a slide deck that needed fixing
        // comes back alongside the worked solutions derived from it.
        //
        // The model is passed in because some work reads the state of the repository
        // rather than only the file it is making: a translated glossary is assembled from
        // the dictionary in that language before anything is asked of a model.
        internal async Task<List<TexSourceModel>> GenerateSyntheticSource(
            GenerationRequest request, ContentModel model)
        {
            SourceMetadata sm = request.Target;

            if (request.Job == GenerationJob.RefreshDictionary)
            {
                return await RefreshDictionary(sm, model);
            }

            if (request.Job == GenerationJob.ExtendDictionary)
            {
                return await ExtendDictionary(sm, model);
            }

            if (request.Job == GenerationJob.RestateGlossary)
            {
                return RestateGlossary(sm, model);
            }

            // the form decides this before the language does. an English file can be the
            // original or the glossary, and those are made in quite different ways - one
            // is written by a model, the other rendered here from data
            switch (sm.Form)
            {
                case SheetForm.Original:
                    return sm.Language == ISO639_3Code.eng
                        ? await GenerateEnglishSyntheticSource(request)
                        : throw new ArgumentException(
                            "there is no such thing as an original in another language");

                case SheetForm.Glossary:
                    return await GenerateGlossary(sm, model);

                case SheetForm.TranslatedGlossary:
                    return await GenerateTranslatedGlossary(sm, model);

                case SheetForm.RetrieveAndConnect:
                    return await GenerateVariant(sm);

                default:
                    return await GenerateForeignLanguageSyntheticSource(request);
            }
        }
    }
}
