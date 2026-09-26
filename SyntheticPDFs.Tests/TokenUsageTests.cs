using Agents.DeepSeek;
using Newtonsoft.Json.Linq;

namespace SyntheticPDFs.Tests
{
    // what each call cost, read off the usage block the API returns beside the answer
    [TestClass]
    public class TokenUsageTests
    {
        // the shape DeepSeek answers in, trimmed to what matters here
        private const String FullResponse = """
            {
              "model": "deepseek-flash",
              "choices": [ { "message": { "content": "\\begin{document}\\end{document}" } } ],
              "usage": {
                "prompt_tokens": 9700,
                "completion_tokens": 1350,
                "prompt_cache_hit_tokens": 6400,
                "prompt_cache_miss_tokens": 3300,
                "completion_tokens_details": { "reasoning_tokens": 1000 }
              }
            }
            """;

        [TestMethod]
        public void EveryFigureTheApiGivesIsRead()
        {
            var usage = DeepSeekBase.UsageFrom(JObject.Parse(FullResponse));

            Assert.IsNotNull(usage);
            Assert.AreEqual("deepseek-flash", usage.Model, "which model answered is how to tell which is billed");
            Assert.AreEqual(9700, usage.PromptTokens);
            Assert.AreEqual(1350, usage.CompletionTokens);
            Assert.AreEqual(6400, usage.CachedPromptTokens);
            Assert.AreEqual(1000, usage.ReasoningTokens);
        }

        [TestMethod]
        public void AFigureTheApiDidNotGiveIsUnknownRatherThanZero()
        {
            // zero would read as "none of it was cached" or "it did no reasoning", which
            // is a measurement nobody took
            var usage = DeepSeekBase.UsageFrom(JObject.Parse("""
                { "model": "deepseek-chat", "usage": { "prompt_tokens": 10, "completion_tokens": 5 } }
                """));

            Assert.IsNotNull(usage);
            Assert.AreEqual(10, usage.PromptTokens);
            Assert.IsNull(usage.CachedPromptTokens);
            Assert.IsNull(usage.ReasoningTokens);
        }

        [TestMethod]
        public void AResponseWithNoUsageReportsNothing()
        {
            Assert.IsNull(DeepSeekBase.UsageFrom(JObject.Parse("""{ "model": "deepseek-chat" }""")));
        }
    }
}
