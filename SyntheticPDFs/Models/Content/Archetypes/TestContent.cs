namespace SyntheticPDFs.Models.Content.Archetypes
{
    // Whatever sits under latex/test/: sample sheets for trying the site out, and the
    // font probe that proves the build container is set up. They are there to be
    // compiled and served as they stand, so nothing is ever derived from them.
    //
    // A plan with only a root in it would not do that. Anything a plan does not name is
    // removed, and the samples are the sheets with everything derived from them already
    // committed - so these are left alone altogether, which also keeps them out of a
    // purge. The plan is empty so that a request for one of them is refused as well.
    internal sealed class TestContent : SheetArchetype
    {
        internal override String Description => "test content";

        internal override String Folder => "test";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[] { SheetPart.Root };

        internal override bool LeftAlone => true;

        internal override IReadOnlyList<PlannedFile> Plan(
            LanguageTable languages, bool includeGlossaries = true) =>
            Array.Empty<PlannedFile>();
    }
}
