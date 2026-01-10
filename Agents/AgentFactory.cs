using Agents.DeepSeek;

namespace Agents
{

    public class MissingAPIKeyException : Exception
    {
        public string missingLocation;

        public MissingAPIKeyException(string missingLocation)
        {
            this.missingLocation = missingLocation;
        }

        public MissingAPIKeyException(string missingLocation, string message)
            : base(message)
        {
            this.missingLocation = missingLocation;
        }

        public MissingAPIKeyException(string missingLocation, string message, Exception innerException)
            : base(message, innerException)
        {
            this.missingLocation = missingLocation;
        }
    }

    public static class AgentFactory
    {
        private static SemaphoreSlim DeepSeekSemaphore = new SemaphoreSlim(20); // Global API lock for DeepSeek
        public static AgentBase GenerateDeepSeekProcessingAgent(
            String systemMessage, LLM model, bool tempZero, string apiKey)
        {

            DeepSeekBase ret = new DeepSeekBase(apiKey, DeepSeekSemaphore);

            if (tempZero)
            {
                ret.ContinuousParameter("Temperature").Value = 0.0;
            }

            // processing, so no history and allows concurrency
            ret.DiscreteParameter("PromptDepth").Value = 0;
            ret.MaxConcurrentResponses = 5; // local concurrency limit

            ret.StringParameters["system"] = systemMessage;

            if (model == LLM.DeepSeek_chat)
            {
                ret.StringParameters["model"] = "deepseek-chat";

                ret.DiscreteParameter("ContextTokens").Value = 16000; // TODO - what are these
                ret.DiscreteParameter("ResponseTokens").Value = 4000; // TODO - ""

            }
            else
            {
                throw new NotImplementedException();
            }

            return ret;
        }
    }
}
