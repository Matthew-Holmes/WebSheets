using Agents;
using Microsoft.Extensions.Options;


namespace SyntheticPDFs.Services
{
    public class LLMService : ILLMService
    {
        private ILogger<LLMService> _logger;

        private readonly LLMOptions _options;

        private static readonly String _systemMessage = "You are a LaTeX source writer, producing .tex file contents that will compile, follow instructions for file contents to generate";

        // its own agent, since a reviewer that answers in one word wants a different brief
        // to a writer that emits whole files
        private static readonly String _yesNoSystemMessage = "You are a LaTeX source reviewer, answer the question with exactly one word, YES or NO, and nothing else";

        private AgentBase Agent { get; init; }

        private AgentBase YesNoAgent { get; init; }

        public LLMService(IOptions<LLMOptions> options, ILogger<LLMService> logger)
        {
            _options = options.Value;

            Agent = AgentFactory.GenerateDeepSeekProcessingAgent(_systemMessage, LLM.DeepSeek_chat, false, _options.DeepSeekAPIKey);

            // temperature zero, since the same deck should get the same verdict twice running
            YesNoAgent = AgentFactory.GenerateDeepSeekProcessingAgent(_yesNoSystemMessage, LLM.DeepSeek_chat, true, _options.DeepSeekAPIKey);

            _logger = logger;
        }

        public async Task<String> GetResponse(String prompt)
        {
            return await Agent.GetResponse(prompt);
        }

        public async Task<bool?> GetYesNoResponse(String question)
        {
            String response = await YesNoAgent.GetResponse(question);

            bool? answer = ParseYesNo(response);

            if (answer is null)
            {
                _logger.LogWarning($"could not read a yes/no answer out of: {response}");
            }

            return answer;
        }

        // models like to add a full stop, or wrap the word in markdown, so read the first
        // word rather than demanding the response be exactly YES or NO
        internal static bool? ParseYesNo(String response)
        {
            String first = new String(response
                .SkipWhile(c => !Char.IsLetter(c))
                .TakeWhile(Char.IsLetter)
                .ToArray());

            if (String.Equals(first, "YES", StringComparison.OrdinalIgnoreCase)) { return true; }

            if (String.Equals(first, "NO", StringComparison.OrdinalIgnoreCase)) { return false; }

            return null;
        }

        public void Log(LogLevel lvl, String message)
        {
            _logger.Log(lvl, message);
        }

    }
}
