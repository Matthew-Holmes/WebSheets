namespace SyntheticPDFs.Configuration
{
    public class GenerationOptions
    {
        public const string SectionName = "Generation";

        // ceiling on files generated in one pass, halved on each git conflict and
        // restored on success - keep it low for a first run against a live repo
        public int MaxFilesPerRun { get; set; } = 30;
    }
}
