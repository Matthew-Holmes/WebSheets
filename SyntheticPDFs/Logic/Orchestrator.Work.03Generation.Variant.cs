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
        // The rewriting is done here, in code. Only the retitling has a model behind it at
        // all, and only when the rewrite finds nothing it recognises. That is the whole
        // point of a variant being a variant: what changes is a handful of words in a
        // handful of titles, or where a slide sits on a page, and a model handed a beamer
        // deck to reproduce with one thing different will also reflow a picture or lose an
        // overlay somewhere in the middle of it - a failure that shows up in a lesson
        // rather than in a build.
        private async Task<List<TexSourceModel>> GenerateVariant(SourceMetadata target)
        {
            SheetVariant? variant = target.Archetype.VariantFor(target.Form);

            if (variant is null)
            {
                throw new NotImplementedException(
                    $"{target.Archetype} has no {target.Form} variant to make");
            }

            // usually the original, but a variant may be made from another variant - the
            // printable deck exists both for the deck as written and for the retitled one
            SourceMetadata from = target with { Form = variant.MadeFrom };

            String original = RepoManager.GetContent(from.FilePath).TexSource;

            String rewritten = target.Form switch
            {
                SheetForm.RetrieveAndConnect =>
                    await Retitled(original, from.FilePath),

                SheetForm.ForPrinting or SheetForm.RetrieveAndConnectForPrinting =>
                    LaidOutForPrinting(original, from.FilePath),

                _ => throw new NotImplementedException($"no rewriter for {target.Form}"),
            };

            return new List<TexSourceModel>
            {
                new TexSourceModel
                {
                    FileNameFullPath = target.FilePath,
                    TexSource        = rewritten,
                },
            };
        }

        private async Task<String> Retitled(String original, String builtFrom)
        {
            String? rewritten = RetrieveAndConnect.Rewrite(original, builtFrom);

            if (rewritten is not null) { return rewritten; }

            _logger.LogInformation(
                "nothing in {File} reads as a starter's title, so a model is being "
                + "asked to retitle it", builtFrom);

            String? asked = await SourceGenerator.RetitleAsRetrieveAndConnect(original, LLMService);

            if (asked is null)
            {
                _logger.LogWarning(
                    "{File} could not be retitled, so its variant is a copy of it", builtFrom);

                return RetrieveAndConnect.Unchanged(original, builtFrom);
            }

            return RetrieveAndConnect.FromAModel(asked, builtFrom);
        }

        // No model is asked anything here, and there is no route by which one could be:
        // the slides are the slides, and all that changes is that each is asked for at
        // its first step and written out four times so that pgfpages can put four of them
        // on a page. A deck this finds no frames in is copied rather than left unmade, so
        // the pass settles instead of coming back to the same file every time.
        private String LaidOutForPrinting(String original, String builtFrom)
        {
            String? printable = ForPrinting.Render(original, builtFrom);

            if (printable is not null) { return printable; }

            _logger.LogWarning(
                "{File} has no slides this could lay out four to a page, so its printable "
                + "version is a copy of it", builtFrom);

            return ForPrinting.Unchanged(original, builtFrom);
        }
    }
}
