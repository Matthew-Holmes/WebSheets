using Microsoft.Extensions.Logging;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Tests.Fakes
{
    // returns scripted responses and records what it was asked
    public class FakeLLMService : ILLMService
    {
        // scripted by predicate, for when one substring isn't enough to say which call this
        // is - checked before ScriptedResponses, first hit wins
        public List<(Func<String, bool> Matches, String Response)> ScriptedWhen { get; } = new();

        // matched against the prompt by substring - first hit wins
        public List<(String PromptContains, String Response)> ScriptedResponses { get; } = new();

        // used when nothing above matches
        public String DefaultResponse { get; set; } = ValidTex("Default");

        public List<String> PromptsSeen { get; } = new();

        // reviews are recorded separately, so a test can tell a verdict apart from a
        // generation without picking through one list
        public Queue<ReviewVerdict?> ReviewSequence { get; } = new();

        public ReviewVerdict? DefaultVerdict { get; set; } = Pass;

        public List<String> QuestionsSeen { get; } = new();

        public String SummaryResponse { get; set; } = "the review thought some answers were not shown properly";

        public List<String> SummaryPromptsSeen { get; } = new();

        // what the code chose to log, so a test can pin what does and does not reach the log
        public List<(LogLevel Level, String Message)> LogEntries { get; } = new();

        public int CallCount => PromptsSeen.Count;

        public int ReviewCallCount => QuestionsSeen.Count;

        public int SummaryCallCount => SummaryPromptsSeen.Count;

        public static ReviewVerdict Pass => new ReviewVerdict { Passed = true, Reasons = String.Empty };

        public static ReviewVerdict Fail(String reasons = "an answer is missing") =>
            new ReviewVerdict { Passed = false, Reasons = reasons };

        public FakeLLMService When(Func<String, bool> matches, String response)
        {
            ScriptedWhen.Add((matches, response));
            return this;
        }

        public Task<String> GetResponse(String prompt)
        {
            lock (PromptsSeen)
            {
                PromptsSeen.Add(prompt);
            }

            foreach (var (matches, response) in ScriptedWhen)
            {
                if (matches(prompt)) { return Task.FromResult(response); }
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

        public Task<ReviewVerdict?> GetReviewResponse(String question)
        {
            lock (QuestionsSeen)
            {
                QuestionsSeen.Add(question);

                if (ReviewSequence.Count > 0)
                {
                    return Task.FromResult(ReviewSequence.Dequeue());
                }
            }

            return Task.FromResult(DefaultVerdict);
        }

        public Task<String> GetSummaryResponse(String prompt)
        {
            lock (SummaryPromptsSeen)
            {
                SummaryPromptsSeen.Add(prompt);
            }

            return Task.FromResult(SummaryResponse);
        }

        public void Log(LogLevel lvl, String message)
        {
            lock (LogEntries)
            {
                LogEntries.Add((lvl, message));
            }
        }

        public IEnumerable<String> Logged => LogEntries.Select(e => e.Message);

        // minimal document that satisfies IsValidTex: starts with a backslash,
        // balanced begin/end, no fences, no untypesettable characters
        public static String ValidTex(String body) =>
            "\\documentclass{article}\n"
            + "\\begin{document}\n"
            + body + "\n"
            + "\\end{document}";
    }
}
