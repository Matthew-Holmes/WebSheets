namespace SyntheticPDFs.Models.Content
{
    // One extra English version of a source file that an archetype offers: the same
    // content with something about it changed for a school that wants it differently,
    // made from a file that already exists rather than written again.
    //
    // Three things vary between them, and all three are stated here rather than assumed,
    // so that adding a variant is a form, a rewriter, and a line on the archetype that
    // wants it. Nothing else has to learn a variant exists in order to plan it, judge it,
    // or name it.
    internal sealed record SheetVariant
    {
        internal required SheetForm Form { get; init; }

        // The form it is made from. Usually the original, but a variant may perfectly
        // well be made from another variant - the printable deck of starters exists both
        // for the deck as written and for the deck retitled the way some schools ask for.
        internal SheetForm MadeFrom { get; init; } = SheetForm.Original;

        // Which parts of the sheet it exists for. Null means every part the archetype
        // has, which is what a retitling wants - the questions and their worked solutions
        // both carry titles. A printable version wants only the questions, since the
        // point of it is a sheet of questions to hand out.
        internal IReadOnlyList<SheetPart>? Parts { get; init; }

        internal IEnumerable<SheetPart> PartsOf(SheetArchetype archetype) =>
            (Parts ?? archetype.Parts).Where(archetype.Has);
    }
}
