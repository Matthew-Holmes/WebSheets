namespace SyntheticPDFs.Models.Content.Archetypes
{
    // A reference sheet for a wall or a folder - the identities, the formulae, the method
    // written out. There is nothing to answer, so it has neither worked solutions nor an
    // answer key, but it is dense with the subject's own words and so is well worth a
    // glossary and a translation.
    internal sealed class Poster : SheetArchetype
    {
        internal override String Description => "a poster";

        internal override String Folder => "cheatSheets";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[] { SheetPart.Root };
    }
}
