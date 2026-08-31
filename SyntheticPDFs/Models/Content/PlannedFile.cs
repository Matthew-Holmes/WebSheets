namespace SyntheticPDFs.Models.Content
{
    // One file the pipeline is allowed to have for a root, and what it is derived from.
    //
    // A plan is the archetype's answer to "which files may this thing have"; staleness
    // and batch selection are both read off it, so a rule such as "a poster has no
    // worked solutions" is stated once, in one class, rather than spread across a
    // staleness check and three batch-selection methods that have to agree.
    internal record PlannedFile
    {
        internal required ContentKey Key { get; init; }

        // every file that must exist, and be no older than this one, for it to be current
        internal required IReadOnlyList<ContentKey> DependsOn { get; init; }

        // written by a person, so the pipeline maintains it but never creates it
        internal bool Written { get; init; }

        // created without being asked for. everything else is created on request only,
        // and once removed as stale is not rebuilt
        internal bool Eager { get; init; }
    }
}
