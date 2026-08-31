using Microsoft.Extensions.Logging;
using Shared;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Git;
using SyntheticPDFs.Logic;
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

    if (result.Problems.Count > 0)
    {
        // The work has been queued either way - a dictionary that will not parse is a
        // reason to tell somebody, not a reason to stop. Answering with an error status
        // is how it gets told: the workflow that pings after a push fails, and whoever
        // pushed sees which file and what is wrong with it, rather than the edit quietly
        // ceasing to be applied.
        logger.LogError(
            "reporting {Count} unreadable file(s) in the content repository",
            result.Problems.Count);

        return Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    return Results.Ok(result);
});

// Which languages this instance can produce. The website reads this rather than
// keeping a list of its own, so it can only ever offer a language that would work.
app.MapGet("/languages", (Orchestrator orchestrator) =>
    Results.Ok(orchestrator.SupportedLanguages()));

// Ask for one translated file. Everything it is derived from is queued with it and
// jumps ahead of the work the pipeline chose for itself, so a caller gets what they
// asked for without having to know what it depends on, or wait for the whole
// repository to be translated first.
app.MapPost("/generate", (
    GenerateRequest request,
    Orchestrator orchestrator,
    ILogger<Program> logger) =>
{
    logger.LogInformation(
        "Received /generate request for {Root} in {Language}",
        request.RootName, request.Language);

    GenerateResult result;

    try
    {
        result = orchestrator.RequestGeneration(request);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception during RequestGeneration()");
        return Results.Problem("Internal server error");
    }

    // a request we could not read is the caller's mistake, and the message says why
    return result.Outcome == GenerateOutcome.NotUnderstood
        ? Results.BadRequest(result)
        : Results.Ok(result);
});

// Remove the generated translations so they are built again from the current settings.
// The provenance block in each file means this is rarely needed - a colour change
// rebuilds only what it affects - but a rework big enough to want the whole lot gone is
// what this is for.
app.MapPost("/l2/purge", async (
    PurgeRequest request,
    Orchestrator orchestrator,
    ILogger<Program> logger) =>
{
    logger.LogWarning("Received /l2/purge request with scope {Scope}", request.Scope);

    PurgeResult result;

    try
    {
        result = await orchestrator.PurgeAsync(request.Scope);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception during PurgeAsync()");
        return Results.Problem("Internal server error");
    }

    logger.LogInformation("Purge removed {Count} file(s)", result.Files.Count);

    return Results.Ok(result);
});

app.Run();
