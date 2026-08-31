namespace Shared
{
    // Ask for one translated file to be generated. Everything it is derived from is
    // queued with it, so a caller can ask for the thing they want without knowing what
    // it depends on.
    public sealed class GenerateRequest
    {
        // path of the English root without its extension, as it appears in the
        // repository - "latex/worksheets/algebra/quadratics"
        public string RootName { get; set; } = "";

        // three letter ISO 639-3 code, "pol"
        public string Language { get; set; } = "";

        // Root, WorkedSolutions or Solutions - which part of the sheet
        public string Type { get; set; } = "Root";

        // ParallelText or Tier3Only
        public string Rendition { get; set; } = "ParallelText";
    }

    public enum GenerateOutcome
    {
        Queued,          // accepted, and generation has been started
        AlreadyPresent,  // the file is already in the repository
        NotUnderstood,   // the language, type or rendition was not one we produce
    }

    public sealed record GenerateResult(
        GenerateOutcome Outcome,
        string Message,

        // every file that will be generated to satisfy this, in the order they will be
        // made - the requested one last
        IReadOnlyList<string> Queued);

    public enum PurgeScope
    {
        // translated files only, leaving the English vocabulary keys in place
        Translations,

        // the vocabulary keys as well, for when the shared definitions have changed
        TranslationsAndVocabulary,
    }

    public sealed class PurgeRequest
    {
        public PurgeScope Scope { get; set; } = PurgeScope.Translations;
    }

    public sealed record PurgeResult(
        bool Removed,
        string Message,
        IReadOnlyList<string> Files);

    // A language the generator is configured to produce. Served rather than duplicated
    // in the website, so there is one list: a language the site offers is one the
    // generator can actually typeset.
    public sealed record LanguageInfo(
        string Code,          // ISO 639-3, "pol"
        string Name,          // as a reader would say it, "Polish"
        bool RightToLeft,
        bool Eager);          // generated for every sheet without being asked
}
