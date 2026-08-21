using Amazon.S3;
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
builder.Services.Configure<WorksheetSourceOptions>(
    builder.Configuration.GetSection(WorksheetSourceOptions.SectionName));
builder.Services.Configure<ObjectStoreCredentialsOptions>(
    builder.Configuration.GetSection(ObjectStoreCredentialsOptions.SectionName));
builder.Services.Configure<SyntheticPdfsTriggerOptions>(
    builder.Configuration.GetSection(SyntheticPdfsTriggerOptions.SectionName));

// SigV4-authenticated S3 client, pointed at the Garage endpoint rather than real AWS.
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var source = sp.GetRequiredService<IOptions<WorksheetSourceOptions>>().Value;
    var credentials = sp.GetRequiredService<IOptions<ObjectStoreCredentialsOptions>>().Value;

    var config = new AmazonS3Config
    {
        ServiceURL = source.ObjectStoreBaseUrl,
        AuthenticationRegion = source.ObjectStoreRegion,
        ForcePathStyle = true, // Garage expects path-style requests: {endpoint}/{bucket}/{key}
    };

    return new AmazonS3Client(credentials.AccessKeyId, credentials.SecretAccessKey, config);
});


builder.Services.AddHttpClient("SyntheticPDFsAPI", (sp, client) =>
{
    var trigger = sp.GetRequiredService<IOptions<SyntheticPdfsTriggerOptions>>().Value;
    client.BaseAddress = new Uri(trigger.BaseUrl);
});


var app = builder.Build();

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


app.Run();
