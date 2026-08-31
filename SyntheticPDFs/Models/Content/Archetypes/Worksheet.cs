namespace SyntheticPDFs.Models.Content.Archetypes
{
    // A sheet of questions to be worked through on paper. The default, and what a file
    // in a folder none of the others claims is assumed to be.
    internal sealed class Worksheet : SheetArchetype
    {
        internal override String Description => "a worksheet";

        internal override String Folder => "worksheets";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[]
        {
            SheetPart.Root,
            SheetPart.WorkedSolutions,
            SheetPart.Solutions,
        };
    }
}
