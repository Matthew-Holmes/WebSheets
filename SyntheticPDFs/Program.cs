using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using Microsoft.Extensions.Logging;
using SyntheticPDFs.Git;
using SyntheticPDFs.Services;
using Shared;

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

        String apiKeyPath = options.DeepSeekAPIKey;

        if (OperatingSystem.IsWindows())
        {
            apiKeyPath = Path.GetFullPath(
                options.DeepSeekAPIKeyFile.Replace('/', Path.DirectorySeparatorChar));
        }

        if (!File.Exists(options.DeepSeekAPIKeyFile))
            throw new FileNotFoundException(
                $"DeepSeek API key file not found: {options.DeepSeekAPIKeyFile}");

        options.DeepSeekAPIKey = File.ReadAllText(options.DeepSeekAPIKeyFile).Trim();

        if (string.IsNullOrWhiteSpace(options.DeepSeekAPIKey))
            throw new InvalidOperationException("Deepseek API key file is empty");
    })
    .Validate(o => !string.IsNullOrWhiteSpace(o.DeepSeekAPIKey), "DeepSeek ApiKey not loaded")
    .ValidateOnStart();



// where the worksheet source lives, and which key may push to it
builder.Services
    .AddOptions<ContentRepositoryOptions>()
    .Bind(builder.Configuration.GetSection(ContentRepositoryOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.CloneUrl),
        $"{ContentRepositoryOptions.SectionName}:CloneUrl is not configured")
    .Validate(o => !string.IsNullOrWhiteSpace(o.PushUrl),
        $"{ContentRepositoryOptions.SectionName}:PushUrl is not configured")
    .Validate(o => !string.IsNullOrWhiteSpace(o.LocalDirectory),
        $"{ContentRepositoryOptions.SectionName}:LocalDirectory is not configured")
    .Validate(o => !string.IsNullOrWhiteSpace(o.SourceDirectory),
        $"{ContentRepositoryOptions.SectionName}:SourceDirectory is not configured")
    .Validate(o => !string.IsNullOrWhiteSpace(o.SshKeyPath),
        $"{ContentRepositoryOptions.SectionName}:SshKeyPath is not configured")
    .ValidateOnStart();


builder.Services
    .AddOptions<GenerationOptions>()
    .Bind(builder.Configuration.GetSection(GenerationOptions.SectionName))
    .Validate(o => o.MaxFilesPerRun > 0,
        $"{GenerationOptions.SectionName}:MaxFilesPerRun must be at least 1")
    .ValidateOnStart();


// colours, shared definitions and the language table for the translated sheets.
// an unusable language is warned about and skipped rather than stopping the
// service, since the English pipeline does not depend on any of this
builder.Services
    .AddOptions<L2Options>()
    .Bind(builder.Configuration.GetSection(L2Options.SectionName))
    .ValidateOnStart();


builder.Services.AddSingleton<Orchestrator>();
builder.Services.AddSingleton<IGitRepoManager, GitRepoManager>();
builder.Services.AddSingleton<ILLMService, LLMService>();


// only listen on local host - let the main website call it
int apiPort = builder.Configuration.GetValue<int?>("Api:Port")
    ?? throw new InvalidOperationException("Api:Port is not configured");

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(apiPort);
});

var app = builder.Build();

var orchestrator = app.Services.GetRequiredService<Orchestrator>(); // force startup

app.MapPost("/ping", (
    PingRequest _,
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
        PingOutcome.Ignored => Results.Ok(result),
        _ => Results.Ok(result)
    };
});

// TODO - ping a sheet and a language to kick off source generation for that!
// Since we won't do that automatically (or a not least for every language!)

app.Run();
