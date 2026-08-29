namespace SyntheticPDFs.Services
{
    // seam for testing - lets generation run against scripted responses instead of the live API
    public interface ILLMService
    {
        Task<String> GetResponse(String prompt);

        // a verdict plus why, so a rejection can be logged and explained rather than being a
        // single bit that gets thrown away. null when the model wouldn't commit to a verdict,
        // which is not the same as failing
        Task<ReviewVerdict?> GetReviewResponse(String question);

        // short prose that ends up in front of a person rather than in a .tex file
        Task<String> GetSummaryResponse(String prompt);

        void Log(LogLevel lvl, String message);
    }
}
