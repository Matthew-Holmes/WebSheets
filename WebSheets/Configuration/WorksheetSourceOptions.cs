namespace WebSheets.Configuration
{
    /// <summary>
    /// Locations of the external content this site presents: the object store
    /// serving compiled worksheet PDFs, and the GitHub repository holding their
    /// LaTeX source. Bound from the "WorksheetSource" section of appsettings.json
    /// so they can vary per environment without a code change.
    /// </summary>
    public class WorksheetSourceOptions
    {
        public const string SectionName = "WorksheetSource";

        /// <summary>Base URL of the object store serving manifest.txt and the compiled PDFs.</summary>
        public string ObjectStoreBaseUrl { get; set; } = "";

        /// <summary>Base URL of the GitHub repository holding the LaTeX source.</summary>
        public string GitHubRepoUrl { get; set; } = "";

        /// <summary>Folder within the repository's default branch that holds the .tex source files.</summary>
        public string LatexSourcePath { get; set; } = "latex";
    }
}
