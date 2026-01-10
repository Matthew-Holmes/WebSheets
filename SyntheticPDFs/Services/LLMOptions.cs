namespace SyntheticPDFs.Services
{
    public sealed class LLMOptions
    {
        public String DeepSeekAPIKeyFile { get; init; } = string.Empty;

        // not bound from config, read later
        public String DeepSeekAPIKey { get; internal set; } = string.Empty;
    }
}
