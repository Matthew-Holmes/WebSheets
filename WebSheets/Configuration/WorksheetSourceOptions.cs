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

        /// <summary>Base (S3 API) URL of the object store serving manifest.txt and the compiled PDFs.</summary>
        public string ObjectStoreBaseUrl { get; set; } = "";

        /// <summary>Name of the bucket within the object store that holds manifest.txt and the compiled PDFs.</summary>
        public string ObjectStoreBucketName { get; set; } = "";

        /// <summary>Region string to sign requests with. Garage accepts any value here; it isn't a real AWS region.</summary>
        public string ObjectStoreRegion { get; set; } = "garage";

        /// <summary>
        /// Base URL of the object store's public website listener, which serves the
        /// bucket's contents to anonymous requests (no signing, no bucket name in the
        /// path - Garage identifies the bucket from this hostname). Used for the plain
        /// download links handed to the browser, separate from ObjectStoreBaseUrl,
        /// which is the private, signed S3 API endpoint used server-side.
        /// </summary>
        public string PublicDownloadBaseUrl { get; set; } = "";

        /// <summary>Base URL of the GitHub repository holding the LaTeX source.</summary>
        public string GitHubRepoUrl { get; set; } = "";

        /// <summary>Folder within the repository's default branch that holds the .tex source files.</summary>
        public string LatexSourcePath { get; set; } = "latex";
    }

    /// <summary>
    /// The access key pair used to sign requests to the object store (AWS SigV4,
    /// same scheme as talking to real S3). These are secrets: never put real
    /// values in appsettings.json. Locally, set them with `dotnet user-secrets`;
    /// in production, supply them as environment variables
    /// (ObjectStoreCredentials__AccessKeyId / ObjectStoreCredentials__SecretAccessKey).
    /// </summary>
    public class ObjectStoreCredentialsOptions
    {
        public const string SectionName = "ObjectStoreCredentials";

        public string AccessKeyId { get; set; } = "";

        public string SecretAccessKey { get; set; } = "";
    }
}
