namespace WebSheets.Configuration
{
    /// <summary>
    /// Controls access to the endpoint that triggers a synthetic-source generation
    /// run. That run commits and pushes to the public LaTeX repository and spends
    /// budget at the LLM provider, so the endpoint is not something anonymous
    /// callers should be able to reach - it is key-checked and rate limited.
    ///
    /// The key is a secret: never put a real value in appsettings.json. Locally,
    /// set it with `dotnet user-secrets`; in production, supply it as an
    /// environment variable (SyntheticPdfsTrigger__ApiKey). When it is not
    /// configured the endpoint rejects every request, so a missing value fails
    /// closed rather than silently reopening the endpoint.
    /// </summary>
    public class SyntheticPdfsTriggerOptions
    {
        public const string SectionName = "SyntheticPdfsTrigger";

        /// <summary>Header the caller puts the key in.</summary>
        public const string ApiKeyHeader = "X-WebSheets-Trigger-Key";

        /// <summary>Shared secret the caller must present in <see cref="ApiKeyHeader"/>.</summary>
        public string ApiKey { get; set; } = "";
    }
}
