namespace WebSheets.Configuration
{

    public class WorksheetSourceOptions
    {
        public const string SectionName = "WorksheetSource";

        // anonymous endpoint serving manifest.txt and the compiled PDFs
        // used for the manifest read and the plain download links handed to the browser
        public string PublicDownloadBaseUrl { get; set; } = "";

        public string GitHubRepoUrl { get; set; } = "";

        /// folder within the repository's default branch that holds the .tex source files
        public string LatexSourcePath { get; set; } = "latex";
    }
}
