using Shared;

namespace WebSheets.Models
{
    // What one folder of the store holds, read from the names in it.
    //
    // Working out what is in a folder means looking a little way below it as well: a
    // sheet's translations live in a folder named after the sheet rather than beside it,
    // and the shared dictionary's live in an L2 folder of their own. Those rules were
    // written for the browse page, but the coverage page has to count exactly the same
    // files, so they live here rather than in either page - two readings of the same
    // names would drift apart, and the first anybody would know of it is a percentage
    // that disagrees with the listing.
    public sealed class WorksheetFolder
    {
        public required IReadOnlyList<WorksheetGroup> Groups { get; init; }

        // The sub-folders worth browsing to. A folder holding nothing but translations
        // is not one of them: its contents belong to the sheet beside it and are reached
        // through that sheet's own menu.
        public required IReadOnlyList<FileNode> Folders { get; init; }

        // The shared dictionary in each language. It belongs to the repository rather
        // than to any sheet, so it has no group to sit inside.
        public required IReadOnlyList<WorksheetFile> Dictionaries { get; init; }

        public static WorksheetFolder Read(
            FileNode? node, string path, IReadOnlyList<LanguageInfo> languages)
        {
            List<WorksheetFile> here = new();
            List<WorksheetFile> translations = new();
            List<FileNode> folders = new();

            if (node is null) { return Empty; }

            foreach (FileNode child in node.Children.Values)
            {
                if (child.IsDirectory)
                {
                    if (child.Name == WorksheetNaming.TranslationFolder)
                    {
                        // an L2 folder sitting here rather than inside a sheet's own
                        // folder holds translations of what is in this folder, which is
                        // how the shared dictionary is laid out
                        translations.AddRange(TranslationsIn(child, path, languages));
                    }
                    else if (IsTranslationFolder(child))
                    {
                        translations.AddRange(TranslationsUnder(child, path, languages));
                    }
                    else
                    {
                        folders.Add(child);
                    }

                    continue;
                }

                WorksheetFile? file = WorksheetNaming.Parse(child.Name, path, languages);

                if (file is not null) { here.Add(file); }
            }

            return new WorksheetFolder
            {
                Folders = folders
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),

                Dictionaries = translations
                    .Where(f => f.Form == SheetForm.Dictionary)
                    .ToList(),

                Groups = WorksheetGroup.Build(
                    here, translations.Where(f => f.Form != SheetForm.Dictionary)),
            };
        }

        // This folder and every folder below it, for anything that has to count the
        // whole store rather than show one page of it. Translation folders are already
        // read by the folder they hang off, so walking the browsable folders reaches
        // every file exactly once.
        public static IEnumerable<WorksheetFolder> Everything(
            FileNode? node, string path, IReadOnlyList<LanguageInfo> languages)
        {
            WorksheetFolder folder = Read(node, path, languages);

            yield return folder;

            foreach (FileNode below in folder.Folders)
            {
                foreach (WorksheetFolder deeper in
                    Everything(below, Join(path, below.Name), languages))
                {
                    yield return deeper;
                }
            }
        }

        private static readonly WorksheetFolder Empty = new()
        {
            Groups       = Array.Empty<WorksheetGroup>(),
            Folders      = Array.Empty<FileNode>(),
            Dictionaries = Array.Empty<WorksheetFile>(),
        };

        // "<sheetName>/L2/<code>/..." - a folder holding nothing but translations
        private static bool IsTranslationFolder(FileNode folder) =>
            folder.Children.Count > 0
            && folder.Children.Keys.All(name => name == WorksheetNaming.TranslationFolder);

        private static IEnumerable<WorksheetFile> TranslationsUnder(
            FileNode sheetFolder, string path, IReadOnlyList<LanguageInfo> languages)
        {
            string parent = Join(path, sheetFolder.Name);

            foreach (FileNode l2 in sheetFolder.Children.Values)
            {
                foreach (WorksheetFile file in TranslationsIn(l2, parent, languages))
                {
                    yield return file;
                }
            }
        }

        // everything below one L2 folder, whatever that folder happens to hang off
        private static IEnumerable<WorksheetFile> TranslationsIn(
            FileNode l2, string parentPath, IReadOnlyList<LanguageInfo> languages)
        {
            foreach (FileNode languageFolder in l2.Children.Values)
            {
                string directory = Join(parentPath, l2.Name, languageFolder.Name);

                foreach (FileNode file in languageFolder.Children.Values)
                {
                    if (file.IsDirectory) { continue; }

                    WorksheetFile? parsed =
                        WorksheetNaming.Parse(file.Name, directory, languages);

                    if (parsed is not null) { yield return parsed; }
                }
            }
        }

        public static string Join(params string[] parts) =>
            string.Join('/', parts.Where(p => p.Length > 0));
    }
}
