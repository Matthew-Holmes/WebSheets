using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Models.Content.Archetypes
{
    // A deck of starter questions, projected rather than printed. The answers belong in
    // the deck itself - each question slide holds its answer back to its second overlay,
    // so it appears on the next slide of the compiled pdf - which is why there is no
    // separate answer key, and why the deck itself has to be checked for the helpers that
    // do the revealing.
    internal sealed class QuestionSlides : SheetArchetype
    {
        internal override String Description => "a deck of question slides";

        internal override String Folder => "starters";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[]
        {
            SheetPart.Root,
            SheetPart.WorkedSolutions,
        };

        internal override bool RevealsItsOwnAnswers => true;

        internal override bool TranslatedEagerly => true;

        // Some schools have their own name for a starter and expect to see it on the
        // board, so the deck and its worked solutions each get a version titled the way
        // they say it. Nothing but the titles differs - see RetrieveAndConnect.
        //
        // A deck is also worth having on paper, for a pupil who was away or who cannot
        // see the board, so each deck of questions gets a version laid out four copies of
        // a slide to a page - see ForPrinting. Only the questions: the worked solutions
        // are a deck to talk through, not a sheet to hand out. The retitled deck gets one
        // too, since a school that renames its starters prints them under that name.
        internal override IReadOnlyList<SheetVariant> Variants { get; } = new[]
        {
            new SheetVariant { Form = SheetForm.RetrieveAndConnect },

            new SheetVariant
            {
                Form  = SheetForm.ForPrinting,
                Parts = new[] { SheetPart.Root },
            },

            new SheetVariant
            {
                Form     = SheetForm.RetrieveAndConnectForPrinting,
                MadeFrom = SheetForm.RetrieveAndConnect,
                Parts    = new[] { SheetPart.Root },
            },
        };

        // A translated slide holds both languages, and a glossed one lifts a translation
        // above every tier 3 word, so a starter that filled the board in English will not
        // fit on one slide once either has been added to it. A slide cannot run on the way
        // a worksheet can, so both translated versions of a deck split each starter across
        // three slides instead - see SplitStartersAcrossSlides.
        internal override String? TranslatedSheetInstructions =>
            SourceGenerator.SplitStartersAcrossSlides;

        // a deck's worked solutions are laid out quite differently to a worksheet's -
        // interleaved with the questions, one solution to a slide - so the prompt that
        // writes them needs saying so. The wording lives with the other prompts; which
        // prompt this archetype wants is what belongs here.
        internal override String? WorkedSolutionsInstructions =>
            SourceGenerator.QuestionSlidesWorkedSolutionRequirements;
    }
}
