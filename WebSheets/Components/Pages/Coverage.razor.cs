using Microsoft.AspNetCore.Components;
using Shared;
using WebSheets.Models;
using WebSheets.Services;

namespace WebSheets.Components.Pages;

// How much of what could be translated has been, language by language.
//
// It answers one question - "how far along is Arabic?" - and it answers it out of the
// same rules the EAL page shows a reader, so a row saying 70% means seventy of every
// hundred entries that page would offer are black rather than grey.
//
// Languages with nothing at all are counted but folded away behind a line saying how
// many there are. There are more than fifty of them and only a handful are generated
// without being asked, so listing them all would bury the ones being worked on under a
// wall of zeroes - but leaving them out entirely would hide that they are on offer.
public partial class Coverage : ComponentBase
{
    [Inject] public ManifestService Manifest { get; set; } = default!;
    [Inject] public LanguageCatalogue Languages { get; set; } = default!;

    protected TranslationCoverage? Counted;

    protected bool Loaded;

    protected CoverageScope Scope = CoverageScope.Sheet;

    // whether the languages nothing has been made in yet are on show
    protected bool ShowingEmpty;

    protected override async Task OnParametersSetAsync()
    {
        IReadOnlyList<LanguageInfo> languages = Languages.Get();

        FileNode tree = await Manifest.GetTreeAsync();

        List<WorksheetFolder> folders =
            WorksheetFolder.Everything(tree, "", languages).ToList();

        Counted = TranslationCoverage.Of(
            folders.SelectMany(f => f.Groups),
            folders.SelectMany(f => f.Dictionaries),
            languages);

        Loaded = true;
    }

    #region What the reader has asked for

    protected static readonly IReadOnlyList<CoverageScope> Scopes = new[]
    {
        CoverageScope.Sheet,
        CoverageScope.WorkedSolutions,
        CoverageScope.Answers,
        CoverageScope.Everything,
    };

    protected void Show(CoverageScope scope) => Scope = scope;

    // On the button, where it has to be short.
    protected static string NameOf(CoverageScope scope) => scope switch
    {
        CoverageScope.Sheet           => "The sheets",
        CoverageScope.WorkedSolutions => "Worked solutions",
        CoverageScope.Answers         => "Answers",
        _                             => "Everything",
    };

    // In the sentence under the headline figure, where it has to read as English.
    protected static string DescriptionOf(CoverageScope scope) => scope switch
    {
        CoverageScope.Sheet           => "the sheets themselves",
        CoverageScope.WorkedSolutions => "the worked solutions",
        CoverageScope.Answers         => "the answer keys",
        _                             => "every part of every sheet",
    };

    protected static string HeadingFor(SheetForm form) => form switch
    {
        SheetForm.LanguageKey  => "Glossary",
        SheetForm.ParallelText => "Parallel text",
        _                      => "Key words only",
    };

    #endregion

    #region The rows

    // Most complete first, so what is nearly done and what has stalled are both at the
    // top rather than found by scrolling. Alphabetical within a percentage, because
    // nothing else about two languages at the same percentage makes one come first.
    protected IEnumerable<LanguageCoverage> Rows()
    {
        if (Counted is null) { return Array.Empty<LanguageCoverage>(); }

        return Counted.Languages
            .OrderByDescending(l => l.Total(Scope).Percent)
            .ThenByDescending(l => l.Anything)
            .ThenBy(l => l.Language.Name, StringComparer.OrdinalIgnoreCase);
    }

    protected IEnumerable<LanguageCoverage> Shown() =>
        ShowingEmpty ? Rows() : Rows().Where(l => l.Anything);

    protected int Empty => Counted is null
        ? 0
        : Counted.Languages.Count - Counted.LanguagesWithSomething;

    protected void ToggleEmpty() => ShowingEmpty = !ShowingEmpty;

    #endregion

    // Why a cell is a dash rather than a nought. A glossary covers a whole sheet, so
    // there is not one for each part of it - which is a different thing from there being
    // none, and the cell has to say which.
    protected static string Describe(CoverageCount count) =>
        count.Applies
            ? $"{count.Made} of {count.Possible} made"
            : "a glossary covers the whole sheet, so there is not one for each part of it";
}
