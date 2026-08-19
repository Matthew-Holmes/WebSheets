using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using WebSheets.Configuration;
using WebSheets.Models;
using WebSheets.Services;

namespace WebSheets.Components.Pages;

public partial class Browse : ComponentBase
{
    [Inject] public ManifestService Manifest { get; set; } = default!;
    [Inject] public NavigationManager Nav { get; set; } = default!;
    [Inject] public IOptions<WorksheetSourceOptions> SourceOptions { get; set; } = default!;

    [Parameter] public string? Path { get; set; }

    protected FileNode? Node;

    protected string CurrentPath => Path ?? "";

    protected override async Task OnParametersSetAsync()
    {
        var tree = await Manifest.GetTreeAsync();
        Node = FindNode(tree, CurrentPath);
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

    protected string FileLink(string name)
    {
        var key = string.IsNullOrEmpty(CurrentPath)
            ? name
            : $"{CurrentPath}/{name}";

        // Worksheets are public content, so this is a plain, permanent URL served
        // by the object store's public website listener - a separate endpoint from
        // the private/signed S3 API, and one that identifies the bucket from the
        // hostname itself, so no bucket name goes in the path here.
        var options = SourceOptions.Value;
        return $"{options.PublicDownloadBaseUrl}/{key}";
    }

    private string ParentDirLink()
    {
        if (string.IsNullOrEmpty(CurrentPath))
            return "/browse";

        var lastSlash = CurrentPath.LastIndexOf('/');
        return lastSlash == -1
            ? "/browse"
            : $"/browse/{CurrentPath[..lastSlash]}";
    }

    private string SourceCodeLink(string name)
    {
        var fullPath = string.IsNullOrEmpty(CurrentPath)
            ? name
            : $"/{CurrentPath}/{name}";

        // Strip file extension by taking the part before the first dot
        String withoutExt = fullPath.Split('.')[0];

        String sourcePath = WorksheetNaming.StripHashSuffix(withoutExt).TrimStart('/');

        var options = SourceOptions.Value;
        return $"{options.GitHubRepoUrl}/tree/main/{options.LatexSourcePath}/{sourcePath}.tex";

    }
}
