using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Agents.DeepSeek
{
    public class DeepSeekBase : ParametrisedAgentBase
    {
        private readonly String _apiKey;
        private readonly HttpClient _httpClient;

        public string? LastReasoningContent { get; private set; }

        public DeepSeekBase(String apiKey, SemaphoreSlim globalSemaphore) : base(globalSemaphore)
        {
            _apiKey = apiKey;
            this._httpClient = new HttpClient();
            this._httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            this._httpClient.Timeout = TimeSpan.FromSeconds(600); // these can take a while!

            // need to change the values in the factory instead of here if planning changes to these!
            DiscreteParameters.Add(new Parameter<int>("PromptDepth", 10, int.MaxValue, 0, 100, 0));
            DiscreteParameters.Add(new Parameter<int>("ContextTokens", 500_000, int.MaxValue, 0, 500_000, 0));
            DiscreteParameters.Add(new Parameter<int>("ResponseTokens", 250_000, int.MaxValue, 0, 250_000, 0));
            DiscreteParameters.Add(new Parameter<int>("RetryCount", 3, int.MaxValue, 0, 10, 0));

            ContinuousParameters.Add(new Parameter<double>("Temperature", 1.0, 2.0, 0.0));
            ContinuousParameters.Add(new Parameter<double>("TopP", 1.0, 1.0, 0.0));
            ContinuousParameters.Add(new Parameter<double>("FrequencyPenalty", 0.0, 1.0, 0.0));
            ContinuousParameters.Add(new Parameter<double>("PresencePenalty", 0.0, 1.0, 0.0));

            StringParameters.Add("system", "You are a helpful assistant");

            StringParameters.Add("model", "deepseek-flash"); // outperforms pro, cheaper, faster!

            // Thinking-mode toggle: "enabled" or "disabled".
            // Effort (only meaningful while thinking is enabled): low | medium | high | xhigh | max
            StringParameters.Add("thinking", "enabled"); // if enabled need to make sure to extract the response - not working now!?
            StringParameters.Add("reasoning_effort", "high");
        }

        private bool ThinkingEnabled =>
            string.Equals(StringParameters["thinking"], "enabled", StringComparison.OrdinalIgnoreCase);

        protected override async Task<String> GetResponseCore(string prompt)
        {
            var messages = new List<object>();

            // assuming _preamble is a system message about the assistant's role
            messages.Add(new { role = "system", content = StringParameters["system"] });

            // add chat history to messages
            int start = Math.Max(0, SequentialPromptLog.Count - DiscreteParameter("PromptDepth").Value);
            for (int i = start; i < SequentialPromptLog.Count; i++)
            {
                var entry = SequentialPromptLog[i];

                if (!string.IsNullOrEmpty(entry.Item1))
                {
                    messages.Add(new { role = "user", content = entry.Item1 });
                }
                if (!string.IsNullOrEmpty(entry.Item2))
                {
                    // Note: between plain user turns (no "tools" on the request) DeepSeek
                    // ignores any reasoning_content we send back, so replaying just the
                    // final answer here is correct even with thinking mode on.
                    messages.Add(new { role = "assistant", content = entry.Item2 });
                }
            }

            // add the current user prompt
            messages.Add(new { role = "user", content = prompt });

            var data = new Dictionary<string, object?>
            {
                ["model"] = StringParameters["model"],
                ["messages"] = messages.ToArray(),
                ["max_tokens"] = DiscreteParameter("ResponseTokens").Value,
                ["thinking"] = new { type = StringParameters["thinking"] },
                ["reasoning_effort"] = StringParameters["reasoning_effort"]
            };

            // Thinking mode doesn't support temperature/top_p/frequency_penalty/presence_penalty
            // (DeepSeek silently ignores them rather than erroring), so only send them when
            // thinking mode is off.
            if (!ThinkingEnabled)
            {
                data["temperature"] = ContinuousParameter("Temperature").Value;
                data["top_p"] = ContinuousParameter("TopP").Value;
                data["frequency_penalty"] = ContinuousParameter("FrequencyPenalty").Value;
                data["presence_penalty"] = ContinuousParameter("PresencePenalty").Value;
            }

            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

            HttpResponseMessage? response;

            for (int retryCnt = 0; /* will throw/break */; retryCnt++)
            {
                try
                {
                    response = await _httpClient.PostAsync("https://api.deepseek.com/chat/completions", content);
                    break;
                }
                catch (Exception e)
                {
                    if (retryCnt > DiscreteParameter("RetryCount").Value)
                    {
                        throw;
                    }
                    await Task.Delay(1000); // long wait since don't want to overload the API
                }
            }

            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
                var message = jsonResponse.choices[0].message;

                // Present (and same-level as "content") only when thinking mode is enabled.
                LastReasoningContent = message.reasoning_content?.ToString();

                // TODO - could request multiple choices for some error prone tasks??
                string modelResponse = message.content.ToString().Trim();

                if (UsageFrom((JObject)jsonResponse) is TokenUsage usage)
                {
                    ReportUsage(usage);
                }

                return modelResponse;
            }
            else
            {
                throw new ApiException($"API Error: {responseString}", response.StatusCode);
            }
        }

        // The usage block DeepSeek puts beside the answer. Beyond the two counts every
        // provider gives, it says how much of the prompt was a cache hit, and how much of
        // the completion was reasoning - which is billed as output but never seen in it.
        public static TokenUsage? UsageFrom(JObject response)
        {
            if (response["usage"] is not JObject usage) { return null; }

            return new TokenUsage
            {
                Model              = (string?)response["model"] ?? "unknown",
                PromptTokens       = (int?)usage["prompt_tokens"] ?? 0,
                CompletionTokens   = (int?)usage["completion_tokens"] ?? 0,
                CachedPromptTokens = (int?)usage["prompt_cache_hit_tokens"],
                ReasoningTokens    = (int?)usage.SelectToken("completion_tokens_details.reasoning_tokens"),
            };
        }
    }

}