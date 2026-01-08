using SyntheticPDFs.Logic;
using Microsoft.Extensions.Logging;
using SyntheticPDFs.Git;

var builder = WebApplication.CreateBuilder(args);

// Configure logging (optional customization)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);



builder.Services.AddSingleton<Orchestrator>();
builder.Services.AddSingleton<GitRepoManager>();


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
