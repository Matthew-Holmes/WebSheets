using SyntheticPDFs.Models.Content.Archetypes;
using System.Reflection;

namespace SyntheticPDFs.Models.Content
{
    // The archetypes there are, and how a path finds the one it belongs to.
    //
    // Found by reflection rather than listed here, so that adding an archetype is a
    // matter of writing its class file - there is no register to remember to update, and
    // no way to add one that the pipeline then never sees. Each is a singleton, so the
    // archetype on a piece of metadata compares by reference.
    internal static class SheetArchetypes
    {
        internal static IReadOnlyList<SheetArchetype> All { get; } = Discover();

        // what a file in a folder none of them claims is treated as
        internal static SheetArchetype Fallback { get; } = Of<Archetypes.Worksheet>();

        internal static SheetArchetype Worksheet { get; } = Of<Archetypes.Worksheet>();

        internal static SheetArchetype QuestionSlides { get; } = Of<Archetypes.QuestionSlides>();

        internal static SheetArchetype Poster { get; } = Of<Archetypes.Poster>();

        internal static SheetArchetype SharedDictionary { get; } = Of<MathematicalDictionary>();

        private static IReadOnlyList<SheetArchetype> Discover()
        {
            List<SheetArchetype> found = typeof(SheetArchetype).Assembly
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(SheetArchetype)) && !t.IsAbstract)
                .Select(t => (SheetArchetype)Activator.CreateInstance(t, nonPublic: true)!)
                .OrderBy(a => a.Name, StringComparer.Ordinal)
                .ToList();

            var folders = found
                .GroupBy(a => a.Folder, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToList();

            if (folders.Count > 0)
            {
                // two archetypes claiming one folder makes which one a file belongs to a
                // matter of which was loaded first, so say so at startup rather than
                // letting it be discovered as a file quietly treated as the wrong thing
                throw new InvalidOperationException(
                    "more than one archetype claims the folder(s) "
                    + String.Join(", ", folders.Select(g => $"'{g.Key}'"))
                    + " - each archetype needs a folder of its own");
            }

            return found;
        }

        private static SheetArchetype Of<T>() where T : SheetArchetype =>
            All.OfType<T>().Single();

        internal static SheetArchetype? ByName(String name) =>
            All.FirstOrDefault(a => a.Name == name);

        internal static SheetArchetype? ByFolder(String folder) =>
            All.FirstOrDefault(a => a.Folder == folder);

        // The archetype lives in the folder, not in the filename - "latex/starters/..."
        // is a deck of question slides, "latex/cheatSheets/..." is a poster, and so on.
        //
        // Anything unrecognised falls back to a worksheet with a warning rather than
        // throwing, since one odd path must not take a whole pass down with it.
        internal static SheetArchetype ForPath(String pathNoExt, ILogger? logger = null)
        {
            // git reports paths with '/' on every platform, and RepoLogParser only keeps
            // paths under the source directory, so splitting on '/' is correct on Windows
            String[] levels = pathNoExt.Split('/');

            if (levels[0] != ContentNaming.SourceDirectoryName)
            {
                logger?.LogWarning(
                    "'{File}' is not under '{SourceDir}' - has the structure of the repository "
                    + "changed? treating it as {Archetype}.",
                    pathNoExt, ContentNaming.SourceDirectoryName, Fallback.Description);

                return Fallback;
            }

            if (levels.Length < 3)
            {
                // "latex/atype/nameofsource" is the bare minimum, so there is no folder
                logger?.LogWarning(
                    "'{File}' has no archetype folder, treating it as {Archetype}.",
                    pathNoExt, Fallback.Description);

                return Fallback;
            }

            SheetArchetype? archetype = ByFolder(levels[1]);

            if (archetype is null)
            {
                logger?.LogWarning(
                    "unexpected folder seen: {Folder}, treating '{File}' as {Archetype}.",
                    levels[1], pathNoExt, Fallback.Description);

                return Fallback;
            }

            return archetype;
        }

        // Everything a path means, in one call: which archetype claims it, and then that
        // archetype's own reading of the rest of the name.
        internal static SourceMetadata Parse(String pathNoExt, ILogger? logger = null) =>
            ForPath(pathNoExt, logger).Parse(pathNoExt, logger);
    }
}
