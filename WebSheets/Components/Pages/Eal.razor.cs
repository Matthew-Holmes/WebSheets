using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Shared;
using WebSheets.Configuration;
using WebSheets.Models;
using WebSheets.Services;

namespace WebSheets.Components.Pages;

// Every translated form of one sheet, for whichever language a reader picks.
//
// Forms that have not been generated are shown greyed rather than left out, because
// leaving them out makes an absence indistinguishable from a language we do not do at
// all. Asking for one puts it at the front of the generator's queue.
public partial class Eal : ComponentBase
{
    [Inject] public ManifestService Manifest { get; set; } = default!;
    [Inject] public LanguageCatalogue Languages { get; set; } = default!;
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public IOptions<WorksheetSourceOptions> SourceOptions { get; set; } = default!;
    [Inject] public ILogger<Eal> Logger { get; set; } = default!;

    // the sheet, without extension - "starters/targeted/KS3/circles/circlesArea"
    [Parameter] public string? Path { get; set; }

    protected IReadOnlyList<LanguageInfo> AllLanguages = Array.Empty<LanguageInfo>();

    protected WorksheetGroup? Group;

    protected bool Loaded;

    // what the reader has typed into the language box
    protected string Search = "";

    protected LanguageInfo? Chosen;

    // the form a reader has clicked that does not exist yet, awaiting their confirmation
    protected (SheetPart Part, SheetForm Form)? Pending;

    protected string? Notice;

    protected bool Requesting;

    protected string RootName => (Path ?? "").Split('/').LastOrDefault() ?? "";

    protected string Directory
    {
        get
        {
            string path = Path ?? "";
            int lastSlash = path.LastIndexOf('/');

            return lastSlash < 0 ? "" : path[..lastSlash];
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        AllLanguages = Languages.Get();

        var tree = await Manifest.GetTreeAsync();

        Group = BuildGroup(tree);

        Loaded = true;
    }

    // Reads just this sheet out of the tree: the English files beside it, and the
    // translations in the folder below it.
    private WorksheetGroup? BuildGroup(FileNode tree)
    {
        FileNode? directory = Find(tree, Directory);

        if (directory is null) { return null; }

        List<WorksheetFile> here = new();
        List<WorksheetFile> translations = new();

        foreach (FileNode child in directory.Children.Values)
        {
            if (child.IsDirectory) { continue; }

            WorksheetFile? file = WorksheetNaming.Parse(child.Name, Directory, AllLanguages);

            if (file is not null && file.RootName == RootName) { here.Add(file); }
        }

        if (directory.Children.TryGetValue(RootName, out FileNode? sheetFolder))
        {
            translations.AddRange(TranslationsUnder(sheetFolder));
        }

        return WorksheetGroup.Build(here, translations)
            .FirstOrDefault(g => g.RootName == RootName)
            ?? new WorksheetGroup { RootName = RootName };
    }

    private IEnumerable<WorksheetFile> TranslationsUnder(FileNode sheetFolder)
    {
        foreach (FileNode l2 in sheetFolder.Children.Values)
        {
            foreach (FileNode languageFolder in l2.Children.Values)
            {
                string directory = string.Join('/', new[]
                {
                    Directory, sheetFolder.Name, l2.Name, languageFolder.Name,
                }.Where(p => p.Length > 0));

                foreach (FileNode file in languageFolder.Children.Values)
                {
                    if (file.IsDirectory) { continue; }

                    WorksheetFile? parsed =
                        WorksheetNaming.Parse(file.Name, directory, AllLanguages);

                    if (parsed is not null) { yield return parsed; }
                }
            }
        }
    }

    private static FileNode? Find(FileNode root, string path)
    {
        if (string.IsNullOrEmpty(path)) { return root; }

        FileNode current = root;

        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.Children.TryGetValue(part, out FileNode? next)) { return null; }

            current = next;
        }

        return current;
    }

    #region Choosing a language

    // what the reader sees before they have typed anything. the rest are a search away
    private const int ShownUnsearched = 12;

    protected sealed record Shortlist(IReadOnlyList<LanguageInfo> Shown, int Hidden);

    // Matched on the name as well as the code, since a reader looking for Urdu is far
    // more likely to type "urd" by accident than on purpose. A language already
    // generated sorts first, so what is there to read comes before what has to be asked
    // for.
    //
    // Listing all of them unasked would be a wall of fifty-odd names to read past, and
    // the one a reader wants is nearly always either already made for this sheet or one
    // of the common few. So an empty box shows those and says how many more there are;
    // typing searches the lot.
    protected Shortlist Matches()
    {
        string search = Search.Trim();

        IEnumerable<LanguageInfo> matching = search.Length == 0
            ? AllLanguages
            : AllLanguages.Where(l =>
                l.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || l.Code.StartsWith(search, StringComparison.OrdinalIgnoreCase));

        List<LanguageInfo> ordered = matching
            .OrderByDescending(l => Group?.HasAnyTranslationIn(l.Code) == true)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (search.Length > 0) { return new Shortlist(ordered, 0); }

        List<LanguageInfo> worthShowing = ordered
            .Where(l => l.Eager || Group?.HasAnyTranslationIn(l.Code) == true)
            .Take(ShownUnsearched)
            .ToList();

        // an instance with nothing marked eager would otherwise show an empty box and
        // no way of knowing there was anything behind it
        if (worthShowing.Count == 0)
        {
            worthShowing = ordered.Take(ShownUnsearched).ToList();
        }

        return new Shortlist(worthShowing, ordered.Count - worthShowing.Count);
    }

    protected void Choose(LanguageInfo language)
    {
        Chosen = language;
        Pending = null;
        Notice = null;
    }

    protected void ClearChoice()
    {
        Chosen = null;
        Pending = null;
        Notice = null;
        Search = "";
    }

    #endregion

    #region Listing the forms

    protected IEnumerable<(SheetPart Part, SheetForm Form, WorksheetFile? File)> Forms()
    {
        if (Group is null || Chosen is null) { yield break; }

        foreach ((SheetPart part, SheetForm form) in Group.TranslatableForms())
        {
            yield return (part, form, Group.Translated(Chosen.Code, part, form));
        }
    }

    protected string FileLink(WorksheetFile file) =>
        $"{SourceOptions.Value.PublicDownloadBaseUrl}/{file.FullPath}";

    protected string SourceLink(WorksheetFile file)
    {
        string withoutExt = file.FullPath.Split('.')[0];

        string sourcePath = WorksheetNaming.StripHashSuffix(withoutExt).TrimStart('/');

        var options = SourceOptions.Value;

        return $"{options.GitHubRepoUrl}/tree/main/{options.LatexSourcePath}/{sourcePath}.tex";
    }

    protected string NameOf(SheetPart part, SheetForm form) =>
        Chosen is null ? "" : WorksheetNaming.TranslatedName(RootName, part, form, Chosen);

    protected string BrowseLink()
    {
        return string.IsNullOrEmpty(Directory) ? "/browse" : $"/browse/{Directory}";
    }

    #endregion

    #region Asking for one

    protected void Ask(SheetPart part, SheetForm form)
    {
        Pending = (part, form);
        Notice = null;
    }

    protected void Cancel()
    {
        Pending = null;
    }

    // Sends the request to the generator, which queues it ahead of the work it would
    // otherwise have chosen. The generator listens on loopback, so this call never
    // leaves the machine - the site is the only way in, and that is what keeps the
    // generator off the open internet rather than a key held in the browser.
    protected async Task ConfirmAsync()
    {
        if (Pending is null || Chosen is null) { return; }

        (SheetPart part, SheetForm form) = Pending.Value;

        Requesting = true;
        Notice = null;

        try
        {
            var http = HttpClientFactory.CreateClient("SyntheticPDFsAPI");

            var request = new GenerateRequest
            {
                // the generator names sheets from the source folder down, which the
                // object store paths leave off
                RootName  = $"{SourceOptions.Value.LatexSourcePath}/{Path}",
                Language  = Chosen.Code,
                Type      = part switch
                {
                    SheetPart.WorkedSolutions => "WorkedSolutions",
                    SheetPart.Solutions       => "Solutions",
                    _                         => "Root",
                },
                Rendition = form == SheetForm.Tier3Only ? "Tier3Only" : "ParallelText",
            };

            var response = await http.PostAsJsonAsync("generate", request);

            var result = await response.Content.ReadFromJsonAsync<GenerateResult>();

            if (result is null || result.Outcome != GenerateOutcome.Queued)
            {
                Logger.LogWarning(
                    "generation request for {Root} in {Language} was refused: {Message}",
                    request.RootName, Chosen.Code, result?.Message ?? "no answer");

                Notice = result?.Message is { Length: > 0 } why
                    ? $"That could not be requested: {why}"
                    : "That could not be requested just now. Please try again later.";
            }
            else
            {
                Logger.LogInformation(
                    "queued {Count} file(s) for {Root} in {Language}",
                    result.Queued.Count, request.RootName, Chosen.Code);

                Notice = result.Queued.Count > 1
                    ? $"Started. {result.Queued.Count} files are being written, since this one "
                      + "is built from others that do not exist yet. Check back in a few minutes."
                    : "Started. Check back in a few minutes.";
            }
        }
        catch (Exception e)
        {
            // the generator may simply not be running - say so plainly rather than
            // showing a reader an error page
            Logger.LogError("could not reach the generator: {Message}", e.Message);

            Notice = "The generator is not answering at the moment. Please try again later.";
        }
        finally
        {
            Requesting = false;
            Pending = null;
        }
    }

    #endregion

    // the translated key seeds every other form, so it is worth naming differently
    protected static string Heading(SheetPart part, SheetForm form) =>
        WorksheetNaming.Describe(part, form);
}
