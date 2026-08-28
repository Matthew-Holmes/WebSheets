using Microsoft.Extensions.Logging;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Tests.Fakes
{
    // returns scripted responses and records what it was asked
    public class FakeLLMService : ILLMService
    {
        // matched against the prompt by substring - first hit wins
        public List<(String PromptContains, String Response)> ScriptedResponses { get; } = new();

        // used when nothing in ScriptedResponses matches
        public String DefaultResponse { get; set; } = ValidTex("Default");

        public List<String> PromptsSeen { get; } = new();

        // yes/no questions are recorded separately, so a test can tell a check apart from
        // a generation without picking through one list
        public List<(String QuestionContains, bool? Answer)> ScriptedYesNo { get; } = new();

        public bool? DefaultYesNo { get; set; } = true;

        public List<String> QuestionsSeen { get; } = new();

        public int CallCount => PromptsSeen.Count;

        public int YesNoCallCount => QuestionsSeen.Count;

        public Task<String> GetResponse(String prompt)
        {
            lock (PromptsSeen)
            {
                PromptsSeen.Add(prompt);
            }

            foreach (var (contains, response) in ScriptedResponses)
            {
                if (prompt.Contains(contains, StringComparison.Ordinal))
                {
                    return Task.FromResult(response);
                }
            }

            return Task.FromResult(DefaultResponse);
        }

        public Task<bool?> GetYesNoResponse(String question)
        {
            lock (QuestionsSeen)
            {
                QuestionsSeen.Add(question);
            }

            foreach (var (contains, answer) in ScriptedYesNo)
            {
                if (question.Contains(contains, StringComparison.Ordinal))
                {
                    return Task.FromResult(answer);
                }
            }

            return Task.FromResult(DefaultYesNo);
        }

        public void Log(LogLevel lvl, String message)
        {
        }

        // minimal document that satisfies IsValidTex: starts with a backslash,
        // balanced begin/end, no fences, no untypesettable characters
        public static String ValidTex(String body) =>
            "\\documentclass{article}\n"
            + "\\begin{document}\n"
            + body + "\n"
            + "\\end{document}";
    }
}
