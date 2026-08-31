using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using WebSheets.Components;
using WebSheets.Configuration;
using WebSheets.Services;

using Shared;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<ManifestService>();
builder.Services.AddSingleton<LanguageCatalogue>();
builder.Services.Configure<WorksheetSourceOptions>(
    builder.Configuration.GetSection(WorksheetSourceOptions.SectionName));
builder.Services.Configure<SyntheticPdfsTriggerOptions>(
    builder.Configuration.GetSection(SyntheticPdfsTriggerOptions.SectionName));

builder.Services.AddHttpClient("SyntheticPDFsAPI", (sp, client) =>
{
    var trigger = sp.GetRequiredService<IOptions<SyntheticPdfsTriggerOptions>>().Value;
    client.BaseAddress = new Uri(trigger.BaseUrl);
});


var app = builder.Build();

// Warm both caches now rather than on the first page view. Neither is awaited: a
// reader should never wait on the object store or the generator, and both of these
// serve whatever they last knew while refreshing behind it. Doing it here just means
// the first person to browse after a restart gets a complete page.
_ = app.Services.GetRequiredService<ManifestService>().GetTreeAsync();
app.Services.GetRequiredService<LanguageCatalogue>();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto
});
    

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();

}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Say so at startup rather than letting a missing key look like a working endpoint.
var triggerOptions = app.Services
    .GetRequiredService<IOptions<SyntheticPdfsTriggerOptions>>().Value;

if (string.IsNullOrWhiteSpace(triggerOptions.ApiKey))
{
    app.Logger.LogWarning(
        "{Section}:{Key} is not configured - the Synthetic PDF trigger will reject every request",
        SyntheticPdfsTriggerOptions.SectionName,
        nameof(SyntheticPdfsTriggerOptions.ApiKey));
}

// Compare hashes rather than the raw values, so that neither the key nor its
// length is recoverable from how long the comparison takes.
static bool IsAuthorisedTrigger(HttpRequest request, string expectedKey)
{
    if (string.IsNullOrWhiteSpace(expectedKey))
        return false; // no key configured, so no caller can be authorised

    if (!request.Headers.TryGetValue(SyntheticPdfsTriggerOptions.ApiKeyHeader, out var provided))
        return false;

    Span<byte> providedHash = stackalloc byte[SHA256.HashSizeInBytes];
    Span<byte> expectedHash = stackalloc byte[SHA256.HashSizeInBytes];

    SHA256.HashData(Encoding.UTF8.GetBytes(provided.ToString()), providedHash);
    SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey), expectedHash);

    return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
}

app.MapPost("/api/public/syntheticPDFs/ping", async (
    HttpContext context,
    IHttpClientFactory factory,
    IOptions<SyntheticPdfsTriggerOptions> trigger,
    PingRequest request,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (!IsAuthorisedTrigger(context.Request, trigger.Value.ApiKey))
    {
        logger.LogWarning(
            "Rejected unauthorised ping from {RemoteIp}",
            context.Connection.RemoteIpAddress);

        return Results.Unauthorized();
    }

    logger.LogInformation("Forwarding ping request");

    var http = factory.CreateClient("SyntheticPDFsAPI");


    HttpResponseMessage response;

    try
    {
        response = await http.PostAsJsonAsync(
            "ping",
            request,
            ct);
    } catch (Exception e)
    {
        logger.LogError($"failed to post to Synthetic PDF API {e.Message}");
        return Results.Problem("Calling internal API threw!");
    }


    if (!response.IsSuccessStatusCode)
    {
        logger.LogWarning(
            "Internal API failed with {Status}",
            response.StatusCode);

        return Results.Problem("Internal API failed");
    }

    var result = await response.Content
        .ReadFromJsonAsync<PingResult>(ct);

    if (result is null)
    {
        logger.LogWarning("recieved null response from Synthetic PDF API!");
    }
    else
    {

        switch (result.Outcome)
        {
            case PingOutcome.Started:
                logger.LogInformation("started Synthetic PDF API");
                break;
            case PingOutcome.Queued:
                logger.LogInformation("queued run of Synthetic PDF API");
                break;
            case PingOutcome.Ignored:
                logger.LogInformation("Synthetic PDF API already has a call queued");
                break;
            default:
                logger.LogWarning("unexpected case hit for result outcome!");
                break;
        }

    }

    return Results.Ok(result);
})
.WithName("PublicSyntheticPdfPing");


// The generation service listens on loopback only, so these two forward to it the same
// way the ping does, behind the same key. Everything is authorisation - a caller either
// holds the key or does not.
static async Task<IResult> Forward<TRequest, TResult>(
    HttpContext context,
    IHttpClientFactory factory,
    IOptions<SyntheticPdfsTriggerOptions> trigger,
    ILogger logger,
    string route,
    TRequest request,
    CancellationToken ct)
{
    if (!IsAuthorisedTrigger(context.Request, trigger.Value.ApiKey))
    {
        logger.LogWarning(
            "Rejected unauthorised {Route} from {RemoteIp}",
            route, context.Connection.RemoteIpAddress);

        return Results.Unauthorized();
    }

    var http = factory.CreateClient("SyntheticPDFsAPI");

    HttpResponseMessage response;

    try
    {
        response = await http.PostAsJsonAsync(route, request, ct);
    }
    catch (Exception e)
    {
        logger.LogError($"failed to post to Synthetic PDF API {e.Message}");
        return Results.Problem("Calling internal API threw!");
    }

    // a rejected request is the caller's to fix, so pass the reason back rather than
    // flattening it into a server error
    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
    {
        return Results.BadRequest(await response.Content.ReadFromJsonAsync<TResult>(ct));
    }

    if (!response.IsSuccessStatusCode)
    {
        logger.LogWarning("Internal API failed with {Status}", response.StatusCode);

        return Results.Problem("Internal API failed");
    }

    return Results.Ok(await response.Content.ReadFromJsonAsync<TResult>(ct));
}

app.MapPost("/api/public/syntheticPDFs/generate", async (
    HttpContext context,
    IHttpClientFactory factory,
    IOptions<SyntheticPdfsTriggerOptions> trigger,
    GenerateRequest request,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    logger.LogInformation(
        "Forwarding generate request for {Root} in {Language}",
        request.RootName, request.Language);

    return await Forward<GenerateRequest, GenerateResult>(
        context, factory, trigger, logger, "generate", request, ct);
})
.WithName("PublicSyntheticPdfGenerate");

app.MapPost("/api/public/syntheticPDFs/l2/purge", async (
    HttpContext context,
    IHttpClientFactory factory,
    IOptions<SyntheticPdfsTriggerOptions> trigger,
    PurgeRequest request,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    logger.LogWarning("Forwarding purge request with scope {Scope}", request.Scope);

    return await Forward<PurgeRequest, PurgeResult>(
        context, factory, trigger, logger, "l2/purge", request, ct);
})
.WithName("PublicSyntheticPdfPurge");


app.Run();
