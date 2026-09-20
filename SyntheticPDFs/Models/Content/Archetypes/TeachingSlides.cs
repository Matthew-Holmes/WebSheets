namespace SyntheticPDFs.Models.Content.Archetypes
{
    // A deck that explains something - the lattice method built up from the area model,
    // say - projected and talked through rather than worked on. It asks nothing, so there
    // is nothing to answer: no worked solutions and no answer key, the same as a poster.
    //
    // What it does have is the subject's own words, used while a class is meeting them for
    // the first time, so it is well worth a glossary and a translation.
    //
    // Not to be confused with QuestionSlides, which is also a deck but is a set of
    // starters to attempt. The folder is what tells them apart: "latex/slides/" explains,
    // "latex/starters/" asks.
    internal sealed class TeachingSlides : SheetArchetype
    {
        internal override String Description => "a deck of teaching slides";

        internal override String Folder => "slides";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[] { SheetPart.Root };
    }
}
