namespace SyntheticPDFs.Models.Content
{
    // What one file in the content repository is, read off its name: which sheet it
    // belongs to, what kind of sheet that is, and which of that sheet's files it is.
    //
    // Immutable, and meant to be moved about with `with` - almost every use is "the same
    // file but the worked solutions", or "the same file but in English", and saying that
    // in one line is what keeps the naming rules in one place.
    internal record SourceMetadata
    {
        internal required SheetPart Part { get; init; }

        internal required SheetArchetype Archetype { get; init; }

        internal required ISO639_3Code Language { get; init; }

        // the English root this belongs to, without extension and without any suffix -
        // "latex/worksheets/algebra/quadratics"
        internal required String RootName { get; init; }

        // defaulted rather than required, since all the English source the pipeline
        // started out generating is the original of its part
        internal SheetForm Form { get; init; } = SheetForm.Original;

        internal ContentKey Key => new(Language, Part, Form);

        // Where this file lives in the repository. Each archetype owns its own naming
        // convention, so a dictionary is not named like a worksheet - asking the metadata
        // rather than a static helper is what keeps that true.
        internal String PathNoExtension => Archetype.FileNameFor(this);

        internal String FilePath => PathNoExtension + ".tex";
    }
}
