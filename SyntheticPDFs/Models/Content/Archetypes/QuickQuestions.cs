namespace SyntheticPDFs.Models.Content.Archetypes
{
    // A deck of quick questions for mini whiteboards. Every question gets two slides: the
    // question on its own, then the same question again with its solution underneath in
    // red. The deck's own \qq macro does that, so the deck is already its own worked
    // solutions - there is nothing to derive, and it has neither worked solutions nor an
    // answer key. It is still worth a glossary and a translation.
    //
    // It does not reveal its answers the way QuestionSlides means by that. That property
    // asks for the \ablank helpers and rewrites a deck that lacks them, which here would
    // mean rewriting a hand-written deck that already shows every solution.
    //
    // Treated as a worksheet before it had a class of its own, which is how one of these
    // came to have worked solutions written as an article, carrying a beamer command that
    // an article cannot compile.
    internal sealed class QuickQuestions : SheetArchetype
    {
        internal override String Description => "a deck of quick questions";

        internal override String Folder => "quickQuestions";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[] { SheetPart.Root };
    }
}
