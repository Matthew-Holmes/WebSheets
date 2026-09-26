namespace Agents
{
    // What one call cost, as the API reported it.
    //
    // The cached and reasoning counts are null when the response did not give them,
    // rather than zero, so a figure the API never sent cannot pass for a measured one.
    public sealed record TokenUsage
    {
        // the model that actually answered, as the API names it - which is not always
        // the one asked for, and is the only way to be sure which one is being billed
        public required String Model { get; init; }

        public int PromptTokens { get; init; }

        public int CompletionTokens { get; init; }

        // the part of the prompt served from the API's cache, which is billed at a
        // fraction of the rest
        public int? CachedPromptTokens { get; init; }

        // the thinking done before answering - billed as completion tokens, and part of
        // CompletionTokens rather than on top of it
        public int? ReasoningTokens { get; init; }
    }
}
