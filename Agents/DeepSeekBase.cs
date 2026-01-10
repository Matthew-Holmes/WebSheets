using System.Text;
using Newtonsoft.Json;

namespace Agents.DeepSeek
{
    public class DeepSeekBase : ParametrisedAgentBase
    {
        private readonly String _apiKey;
        private readonly HttpClient _httpClient;

        public DeepSeekBase(String apiKey, SemaphoreSlim globalSemaphore) : base(globalSemaphore)
        {
            _apiKey = apiKey;
            this._httpClient = new HttpClient();
            this._httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            DiscreteParameters.Add(new Parameter<int>("PromptDepth", 10, int.MaxValue, 0, 100, 0));
            DiscreteParameters.Add(new Parameter<int>("ContextTokens", 1000, int.MaxValue, 0, 16000, 0));
            DiscreteParameters.Add(new Parameter<int>("ResponseTokens", 1000, int.MaxValue, 0, 4000, 0));
            DiscreteParameters.Add(new Parameter<int>("RetryCount", 3, int.MaxValue, 0, 10, 0));

            ContinuousParameters.Add(new Parameter<double>("Temperature", 1.0, 2.0, 0.0));
            ContinuousParameters.Add(new Parameter<double>("TopP", 1.0, 1.0, 0.0));
            ContinuousParameters.Add(new Parameter<double>("FrequencyPenalty", 0.0, 1.0, 0.0));
            ContinuousParameters.Add(new Parameter<double>("PresencePenalty", 0.0, 1.0, 0.0));

            StringParameters.Add("system", "You are a helpful assistant");
            StringParameters.Add("model", "deepseek-chat");
        }


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
                    messages.Add(new { role = "assistant", content = entry.Item2 });
                }
            }

            // add the current user prompt
            messages.Add(new { role = "user", content = prompt });

            var data = new
            {
                model = StringParameters["model"],
                messages = messages.ToArray(),
                temperature = ContinuousParameter("Temperature").Value,
                max_tokens = DiscreteParameter("ResponseTokens").Value,
                top_p = ContinuousParameter("TopP").Value,
                frequency_penalty = ContinuousParameter("FrequencyPenalty").Value,
                presence_penalty = ContinuousParameter("PresencePenalty").Value
            };

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
                        throw e;
                    }
                    Thread.Sleep(1000); // long wait since don't want to overload the API
                }
            }

            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
                // TODO - could request multiple choices for some error prone tasks??
                string modelResponse = jsonResponse.choices[0].message.content.ToString().Trim();

                return modelResponse;
            }
            else
            {
                throw new ApiException($"API Error: {responseString}", response.StatusCode);
            }
        }
    }

}
