using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // A variant: the same file with something about it changed, made from the file
        // rather than written again.
        //
        // The rewrite is done here, in code, and a model is asked only when the rewrite
        // finds nothing it recognises. That is the whole point of a variant being a
        // variant: what is changing is a handful of words in a handful of titles, and a
        // model handed a beamer deck to reproduce with one word different will also
        // reflow a picture or lose an overlay somewhere in the middle of it - a failure
        // that shows up in a lesson rather than in a build.
        private async Task<List<TexSourceModel>> GenerateVariant(SourceMetadata target)
        {
            SourceMetadata from = target with { Form = SheetForm.Original };

            String original = RepoManager.GetContent(from.FilePath).TexSource;

            String? rewritten = target.Form switch
            {
                SheetForm.RetrieveAndConnect =>
                    RetrieveAndConnect.Rewrite(original, from.FilePath),

                _ => throw new NotImplementedException($"no rewriter for {target.Form}"),
            };

            if (rewritten is null)
            {
                _logger.LogInformation(
                    "nothing in {File} reads as a starter's title, so a model is being "
                    + "asked to retitle it", from.FilePath);

                String? asked =
                    await SourceGenerator.RetitleAsRetrieveAndConnect(original, LLMService);

                if (asked is null)
                {
                    _logger.LogWarning(
                        "{File} could not be retitled, so its variant is a copy of it",
                        from.FilePath);
                }

                rewritten = asked is null
                    ? RetrieveAndConnect.Unchanged(original, from.FilePath)
                    : RetrieveAndConnect.FromAModel(asked, from.FilePath);
            }

            return new List<TexSourceModel>
            {
                new TexSourceModel
                {
                    FileNameFullPath = target.FilePath,
                    TexSource        = rewritten,
                },
            };
        }
    }
}
