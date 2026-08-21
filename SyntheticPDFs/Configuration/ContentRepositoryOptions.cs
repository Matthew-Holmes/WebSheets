namespace SyntheticPDFs.Configuration
{
    /// <summary>
    /// The git repository holding the LaTeX worksheet source that this service
    /// reads from and writes generated solutions back to. Bound from the
    /// "ContentRepository" section so the service can be pointed at a fork or a
    /// scratch repository without a code change - useful when testing generation
    /// without committing to the live one.
    ///
    /// None of these are secrets, but <see cref="SshKeyPath"/> locates one, and
    /// its value differs between environments (see the remarks on that property).
    /// </summary>
    public class ContentRepositoryOptions
    {
        public const string SectionName = "ContentRepository";

        /// <summary>HTTPS URL used for the initial clone.</summary>
        public string CloneUrl { get; set; } = "";

        /// <summary>
        /// SSH URL the origin remote is repointed at before pulling or pushing,
        /// since writes authenticate with a deploy key rather than over HTTPS.
        /// </summary>
        public string PushUrl { get; set; } = "";

        /// <summary>
        /// Directory the repository is cloned into, relative to the service's
        /// working directory. Cloned explicitly into this name, so it does not
        /// have to match the repository name in <see cref="CloneUrl"/>.
        /// </summary>
        public string LocalDirectory { get; set; } = "Matthews_Mathematics";

        /// <summary>Folder within the repository holding the .tex source files.</summary>
        public string SourceDirectory { get; set; } = "latex";

        /// <summary>
        /// Path to the private half of the deploy key authorised to push to
        /// <see cref="PushUrl"/>.
        ///
        /// This path is resolved by the shell that runs the git commands, not by
        /// .NET. On Windows that shell is WSL, so the value must be a WSL path
        /// (for example /home/matt/root/.ssh/id_ed25519) rather than a Windows
        /// one - which is why it is set per environment in appsettings rather
        /// than branched on at runtime, and why nothing here checks that the file
        /// exists.
        /// </summary>
        public string SshKeyPath { get; set; } = "";
    }
}
