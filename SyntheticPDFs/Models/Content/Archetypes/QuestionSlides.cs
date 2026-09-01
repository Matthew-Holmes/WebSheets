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

        // Some schools have their own name for a starter and expect to see it on the
        // board, so the deck and its worked solutions each get a version titled the way
        // they say it. Nothing but the titles differs - see RetrieveAndConnect.
        internal override IReadOnlyList<SheetForm> Variants { get; } = new[]
        {
            SheetForm.RetrieveAndConnect,
        };

        // a deck's worked solutions are laid out quite differently to a worksheet's -
        // interleaved with the questions, one solution to a slide - so the prompt that
        // writes them needs saying so. The wording lives with the other prompts; which
        // prompt this archetype wants is what belongs here.
        internal override String? WorkedSolutionsInstructions =>
            SourceGenerator.QuestionSlidesWorkedSolutionRequirements;
    }
}
