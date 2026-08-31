using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Shared;
using WebSheets.Configuration;
using WebSheets.Models;
using WebSheets.Services;

namespace WebSheets.Components.Pages;

public partial class Browse : ComponentBase
{
    [Inject] public ManifestService Manifest { get; set; } = default!;
    [Inject] public LanguageCatalogue Languages { get; set; } = default!;
    [Inject] public NavigationManager Nav { get; set; } = default!;
    [Inject] public IOptions<WorksheetSourceOptions> SourceOptions { get; set; } = default!;

    [Parameter] public string? Path { get; set; }

    protected FileNode? Node;

    protected List<WorksheetGroup> Groups = new();

    protected List<FileNode> Folders = new();

    protected IReadOnlyList<LanguageInfo> KnownLanguages = Array.Empty<LanguageInfo>();

    protected string CurrentPath => Path ?? "";

    protected override async Task OnParametersSetAsync()
    {
        var tree = await Manifest.GetTreeAsync();

        KnownLanguages = Languages.Get();

        Node = FindNode(tree, CurrentPath);

        Regroup();
    }

    // Splits this folder into the sub-folders worth showing and the sheets in it.
    //
    // A sheet's translations live in a folder named after the sheet, so that folder is
    // not somewhere to browse to - its contents belong to the sheet beside it and are
    // reached through the sheet's own menu.
    private void Regroup()
    {
        Groups = new List<WorksheetGroup>();
        Folders = new List<FileNode>();

        if (Node is null) { return; }

        List<WorksheetFile> here = new();
        List<WorksheetFile> translations = new();

        foreach (FileNode child in Node.Children.Values)
        {
            if (child.IsDirectory)
            {
                if (IsTranslationFolder(child))
                {
                    translations.AddRange(TranslationsUnder(child));
                }
                else
                {
                    Folders.Add(child);
                }

                continue;
            }

            WorksheetFile? file = WorksheetNaming.Parse(child.Name, CurrentPath, KnownLanguages);

            if (file is not null) { here.Add(file); }
        }

        Folders = Folders.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Groups = WorksheetGroup.Build(here, translations);
    }

    // "<sheetName>/L2/<code>/..." - a folder holding nothing but translations
    private static bool IsTranslationFolder(FileNode folder) =>
        folder.Children.Count > 0
        && folder.Children.Keys.All(name => name == WorksheetNaming.TranslationFolder);

    private IEnumerable<WorksheetFile> TranslationsUnder(FileNode sheetFolder)
    {
        foreach (FileNode l2 in sheetFolder.Children.Values)
        {
            foreach (FileNode languageFolder in l2.Children.Values)
            {
                string directory = string.Join('/', new[]
                {
                    CurrentPath, sheetFolder.Name, l2.Name, languageFolder.Name,
                }.Where(p => p.Length > 0));

                foreach (FileNode file in languageFolder.Children.Values)
                {
                    if (file.IsDirectory) { continue; }

                    WorksheetFile? parsed =
                        WorksheetNaming.Parse(file.Name, directory, KnownLanguages);

                    if (parsed is not null) { yield return parsed; }
                }
            }
        }
    }

    protected FileNode? FindNode(FileNode root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var part in parts)
        {
            if (!current.Children.TryGetValue(part, out var next))
                return null;

            current = next;
        }

        return current;
    }

    protected string DirLink(string name)
    {
        return string.IsNullOrEmpty(CurrentPath)
            ? $"/browse/{name}"
            : $"/browse/{CurrentPath}/{name}";
    }

    // Worksheets are public content, so this is a plain, permanent URL served
    // by the object store's public website listener - a separate endpoint from
    // the private/signed S3 API, and one that identifies the bucket from the
    // hostname itself, so no bucket name goes in the path here.
    protected string FileLink(WorksheetFile file) =>
        $"{SourceOptions.Value.PublicDownloadBaseUrl}/{file.FullPath}";

    protected string SourceLink(WorksheetFile file)
    {
        // the store holds a pdf built from a .tex of the same name, hash aside
        string withoutExt = file.FullPath.Split('.')[0];

        string sourcePath = WorksheetNaming.StripHashSuffix(withoutExt).TrimStart('/');

        var options = SourceOptions.Value;

        return $"{options.GitHubRepoUrl}/tree/main/{options.LatexSourcePath}/{sourcePath}.tex";
    }

    // the page listing every translated form of one sheet
    protected string EalLink(WorksheetGroup group)
    {
        string path = string.IsNullOrEmpty(CurrentPath)
            ? group.RootName
            : $"{CurrentPath}/{group.RootName}";

        return $"/eal/{path}";
    }

    protected string ParentDirLink()
    {
        if (string.IsNullOrEmpty(CurrentPath))
            return "/browse";

        var lastSlash = CurrentPath.LastIndexOf('/');
        return lastSlash == -1
            ? "/browse"
            : $"/browse/{CurrentPath[..lastSlash]}";
    }
}
