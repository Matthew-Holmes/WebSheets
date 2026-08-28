namespace SyntheticPDFs.Services
{
    // seam for testing - lets generation run against scripted responses instead of the live API
    public interface ILLMService
    {
        Task<String> GetResponse(String prompt);

        // checks only need a verdict, so the caller gets a decision rather than prose
        // null when the model wouldn't commit to one, which is not the same as "no"
        Task<bool?> GetYesNoResponse(String question);

        void Log(LogLevel lvl, String message);
    }
}
