namespace SyntheticPDFs.Services
{
    // seam for testing - lets generation run against scripted responses instead of the live API
    public interface ILLMService
    {
        Task<String> GetResponse(String prompt);

        void Log(LogLevel lvl, String message);
    }
}
