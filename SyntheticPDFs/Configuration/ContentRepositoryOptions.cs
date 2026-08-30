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

        // the shared definitions, held in the content repository so that a wording can be
        // discussed and changed like anything else there. it is a .tex file under the
        // source directory, so it also compiles to a dictionary worth having on its own,
        // and the pipeline derives nothing from it
        public string DictionaryPath { get; set; } = "latex/dictionary/mathematicalDictionary.tex";
    }
}
