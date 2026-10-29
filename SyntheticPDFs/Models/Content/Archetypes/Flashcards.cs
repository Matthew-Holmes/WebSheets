namespace SyntheticPDFs.Models.Content.Archetypes
{
    // A set of revision flashcards, printed double-sided and cut up: a page of questions,
    // then a page of their answers mirrored so that each lands on the back of its own
    // card. The answers are on the cards, so there is nothing to derive - no worked
    // solutions and no answer key.
    //
    // Not translated unasked, like everything but worksheets and starters, but its EAL
    // versions can still be asked for. The cards are sized to the millimetre and flag
    // any whose contents overflow, so a translation may need rules of its own the way a
    // deck of starters did - see QuestionSlides.TranslatedSheetInstructions.
    internal sealed class Flashcards : SheetArchetype
    {
        internal override String Description => "a set of flashcards";

        internal override String Folder => "flashcards";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[] { SheetPart.Root };
    }
}
