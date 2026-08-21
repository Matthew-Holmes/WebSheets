namespace SyntheticPDFs.Configuration
{
    public class ContentRepositoryOptions
    {
        public const string SectionName = "ContentRepository";

        // HTTPS URL used for the initial clone
        public string CloneUrl { get; set; } = "";

        // SSH URL origin is repointed at before pulling or pushing, since writes use a deploy key
        public string PushUrl { get; set; } = "";

        // directory cloned into, relative to the working directory
        // need not match the repo name in CloneUrl, since the clone names it explicitly
        public string LocalDirectory { get; set; } = "Matthews_Mathematics";

        // folder within the repository holding the .tex source files
        public string SourceDirectory { get; set; } = "latex";

        // private half of the deploy key that may push to PushUrl
        // resolved by the shell, not .NET - WSL on Windows, so this takes a WSL path
        public string SshKeyPath { get; set; } = "";
    }
}
