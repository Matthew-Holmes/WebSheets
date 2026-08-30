using Agents;
using Microsoft.Extensions.Options;


namespace SyntheticPDFs.Services
{
    public class LLMService : ILLMService
    {
        private ILogger<LLMService> _logger;

        private readonly LLMOptions _options;

        private static readonly String _systemMessage = "You are a LaTeX source writer, producing .tex file contents that will compile, follow instructions for file contents to generate";

        // its own agent, since a reviewer that has to reach a verdict wants a different brief
        // to a writer that emits whole files
        private static readonly String _reviewSystemMessage = "You are a LaTeX slide reviewer. Put PASS or FAIL on the first line on its own. If it is FAIL, follow it with one short line per thing that is wrong, each one concrete and checkable. Never quote LaTeX source in your reasons, describe what is wrong in words";

        // and again for prose, since notes for a person must not come back as .tex
        private static readonly String _summarySystemMessage = "You write short, plain notes for a teacher. Be concise and concrete, use ordinary prose, and never use LaTeX or markdown";

        // and again for data. the vocabulary keys are typeset from what this returns, so
        // it must be JSON and nothing else - no prose around it and no code fence
        private static readonly String _structuredSystemMessage = "You return a single JSON object and nothing else. No prose before or after it, no markdown code fence, no explanation. Every string must be valid JSON with quotes and backslashes escaped";

        private AgentBase Agent { get; init; }

        private AgentBase ReviewAgent { get; init; }

        private AgentBase SummaryAgent { get; init; }

        private AgentBase StructuredAgent { get; init; }

        public LLMService(IOptions<LLMOptions> options, ILogger<LLMService> logger)
        {
            _options = options.Value;

            Agent = AgentFactory.GenerateDeepSeekProcessingAgent(_systemMessage, LLM.DeepSeek_chat, false, _options.DeepSeekAPIKey);

            // temperature zero, since the same deck should get the same verdict twice running
            ReviewAgent = AgentFactory.GenerateDeepSeekProcessingAgent(_reviewSystemMessage, LLM.DeepSeek_chat, true, _options.DeepSeekAPIKey);

            SummaryAgent = AgentFactory.GenerateDeepSeekProcessingAgent(_summarySystemMessage, LLM.DeepSeek_chat, true, _options.DeepSeekAPIKey);

            // temperature zero, since the same sheet should yield the same vocabulary
            // twice running - a key that changed on every pass would churn the repository
            StructuredAgent = AgentFactory.GenerateDeepSeekProcessingAgent(_structuredSystemMessage, LLM.DeepSeek_chat, true, _options.DeepSeekAPIKey);

            _logger = logger;
        }

        public async Task<String> GetResponse(String prompt)
        {
            return await Agent.GetResponse(prompt);
        }

        public async Task<String> GetSummaryResponse(String prompt)
        {
            return await SummaryAgent.GetResponse(prompt);
        }

        public async Task<String> GetStructuredResponse(String prompt)
        {
            return await StructuredAgent.GetResponse(prompt);
        }

        public async Task<ReviewVerdict?> GetReviewResponse(String question)
        {
            String response = await ReviewAgent.GetResponse(question);

            ReviewVerdict? verdict = ParseReview(response);

            if (verdict is null)
            {
                _logger.LogWarning($"could not read a verdict out of: {response}");
            }

            return verdict;
        }

        // models like to add a full stop, or wrap the word in markdown, so read the first
        // word rather than demanding the response start with exactly PASS or FAIL
        internal static bool? ParseVerdict(String response)
        {
            String first = new String(response
                .SkipWhile(c => !Char.IsLetter(c))
                .TakeWhile(Char.IsLetter)
                .ToArray());

            if (String.Equals(first, "PASS", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (String.Equals(first, "YES", StringComparison.OrdinalIgnoreCase)) { return true; }

            if (String.Equals(first, "FAIL", StringComparison.OrdinalIgnoreCase)) { return false; }
            if (String.Equals(first, "NO", StringComparison.OrdinalIgnoreCase)) { return false; }

            return null;
        }

        internal static ReviewVerdict? ParseReview(String response)
        {
            bool? passed = ParseVerdict(response);

            if (passed is null) { return null; }

            if ((bool)passed)
            {
                return new ReviewVerdict { Passed = true, Reasons = String.Empty };
            }

            // everything after the verdict word is why, with the verdict line itself dropped
            String[] lines = response.Replace("\r\n", "\n").Split('\n');

            String reasons = String.Join('\n', lines.Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0)).Trim();

            if (reasons.Length == 0)
            {
                // a bare FAIL tells whoever reads the log nothing, but it is still a verdict
                reasons = "no reason given";
            }

            return new ReviewVerdict { Passed = false, Reasons = reasons };
        }

        public void Log(LogLevel lvl, String message)
        {
            _logger.Log(lvl, message);
        }

    }
}
