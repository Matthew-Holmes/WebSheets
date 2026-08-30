using SyntheticPDFs.Git;
using SyntheticPDFs.Models;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // one request can settle more than one file - a slide deck that needed fixing comes
        // back alongside the worked solutions derived from it
        internal async Task<List<TexSourceModel>> GenerateSyntheticSource(GenerationRequest request)
        {
            SourceMetadata sm = request.Target;

            // the rendition decides this before the language does. an English file can be
            // the original or the vocabulary key, and those are made in quite different
            // ways - one is written by a model, the other rendered here from data
            switch (sm.Rendition)
            {
                case SourceRendition.Original:
                    return sm.Language == ISO639_3Code.eng
                        ? await GenerateEnglishSyntheticSource(request)
                        : throw new ArgumentException(
                            "there is no such thing as an original in another language");

                case SourceRendition.VocabKey:
                    return await GenerateVocabularyKey(sm);

                case SourceRendition.L2Key:
                    return await GenerateTranslatedKey(sm);

                default:
                    return await GenerateForeignLanguageSyntheticSource(request);
            }
        }
    }
}
