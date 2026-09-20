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

    // the shared dictionary in each language, which belongs to the repository rather
    // than to any sheet and so has no group to sit inside
    protected List<WorksheetFile> Dictionaries = new();

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
    // What counts as which is WorksheetFolder's to say, since the coverage page has to
    // read the same folders the same way. All that is left here is the order they are
    // listed in, which is this page's business and nobody else's.
    private void Regroup()
    {
        WorksheetFolder folder = WorksheetFolder.Read(Node, CurrentPath, KnownLanguages);

        Groups = folder.Groups.ToList();
        Folders = folder.Folders.ToList();

        // a dictionary is not derived from a sheet, so it gets a line of its own rather
        // than hiding inside one sheet's menu of translations
        Dictionaries = folder.Dictionaries
            .OrderBy(DictionaryTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // "Polish Dictionary" - the language list is what turns "pol" into a word, so a
    // dictionary in a language the generator no longer offers is not listed at all
    protected string DictionaryTitle(WorksheetFile file)
    {
        LanguageInfo? language = KnownLanguages.FirstOrDefault(
            l => string.Equals(l.Code, file.LanguageCode, StringComparison.OrdinalIgnoreCase));

        return language is null
            ? file.RootName
            : WorksheetNaming.DictionaryTitle(language);
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
