namespace SyntheticPDFs.Models.Content
{
    // One file git is tracking, with the meaning unpicked from its name.
    //
    // This is the join between the two layers: TrackedFile is all the git abstraction
    // knows - a path and an age in commits - and SourceMetadata is what that path means
    // to the pipeline. Nothing here holds the file's contents; a pass reads those from
    // the repository only for the few files it actually has to look inside.
    internal record ContentFile
    {
        internal required TrackedFile TrackedFile { get; init; }

        internal required SourceMetadata SourceMetadata { get; init; }

        internal String FullPath => TrackedFile.FullPath;

        // lower is younger, since age counts commits back from HEAD
        internal int AgeCommits => TrackedFile.AgeCommits;

        internal ContentKey Key => SourceMetadata.Key;
    }
}
