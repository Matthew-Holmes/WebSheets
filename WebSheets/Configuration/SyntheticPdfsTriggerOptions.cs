namespace WebSheets.Configuration
{
    public class SyntheticPdfsTriggerOptions
    {
        public const string SectionName = "SyntheticPdfsTrigger";

        // header the caller puts the key in
        public const string ApiKeyHeader = "X-WebSheets-Trigger-Key";

        // base URL of the generation service.
        public string BaseUrl { get; set; } = "http://localhost:5432/";

        // shared secret the caller must present in the ApiKeyHeader
        public string ApiKey { get; set; } = "";
    }
}
