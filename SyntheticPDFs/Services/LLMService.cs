using Agents;
using Microsoft.Extensions.Options;


namespace SyntheticPDFs.Services
{
    public class LLMService
    {
        private ILogger<LLMService> _logger;
        
        private readonly LLMOptions _options;

        private static readonly String _systemMessage = "You are a LaTeX source writer, producing .tex file contents that will compile, follow instructions for file contents to generate";

        private AgentBase Agent { get; init; }

        public LLMService(IOptions<LLMOptions> options, ILogger<LLMService> logger)
        {
            _options = options.Value;

            Agent = AgentFactory.GenerateDeepSeekProcessingAgent(_systemMessage, LLM.DeepSeek_chat, false, _options.DeepSeekAPIKey);
            _logger = logger;
        }

        public async Task<String> GetResponse(String prompt)
        {
            return await Agent.GetResponse(prompt);
        }

        public void Log(LogLevel lvl, String message)
        {
            _logger.Log(lvl, message);
        }

    }
}
