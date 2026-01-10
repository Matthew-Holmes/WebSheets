using SyntheticPDFs.Logic;
using Microsoft.Extensions.Logging;
using SyntheticPDFs.Git;
using SyntheticPDFs.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure logging (optional customization)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);


// load up the API keys
builder.Services
    .AddOptions<LLMOptions>()
    .Bind(builder.Configuration.GetSection("LLM"))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.DeepSeekAPIKeyFile))
            throw new InvalidOperationException("LLM:DeepSeekAPIKeyFile is not configured");

        if (!File.Exists(options.DeepSeekAPIKeyFile))
            throw new FileNotFoundException(
                $"DeepSeek API key file not found: {options.DeepSeekAPIKeyFile}");

        options.DeepSeekAPIKey = File.ReadAllText(options.DeepSeekAPIKeyFile).Trim();

        if (string.IsNullOrWhiteSpace(options.DeepSeekAPIKey))
            throw new InvalidOperationException("Deepseek API key file is empty");
    })
    .Validate(o => !string.IsNullOrWhiteSpace(o.DeepSeekAPIKey), "DeepSeek ApiKey not loaded")
    .ValidateOnStart();



builder.Services.AddSingleton<Orchestrator>();
builder.Services.AddSingleton<GitRepoManager>();
builder.Services.AddSingleton<LLMService>();


var app = builder.Build();

var orchestrator = app.Services.GetRequiredService<Orchestrator>(); // force startup

app.MapGet("/ping", (
    Orchestrator orchestrator,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Received /ping request");

    PingResult result;
    try
    {
        result = orchestrator.Ping();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception during Ping()");
        return Results.Problem("Internal server error");
    }

    logger.LogInformation(
        "Ping outcome: {Outcome}",
        result.Outcome);

    return result.Outcome switch
    {
        PingOutcome.Started => Results.Ok(result),
        PingOutcome.Queued => Results.Ok(result),
        PingOutcome.Ignored => Results.StatusCode(409),
        _ => Results.Ok(result)
    };
});

app.Run();
