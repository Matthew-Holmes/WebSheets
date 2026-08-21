using Amazon.S3;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using WebSheets.Components;
using WebSheets.Configuration;
using WebSheets.Services;

using Shared;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<ManifestService>();
builder.Services.Configure<WorksheetSourceOptions>(
    builder.Configuration.GetSection(WorksheetSourceOptions.SectionName));
builder.Services.Configure<ObjectStoreCredentialsOptions>(
    builder.Configuration.GetSection(ObjectStoreCredentialsOptions.SectionName));

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


builder.Services.AddHttpClient("SyntheticPDFsAPI", client =>
{
    client.BaseAddress = new Uri("http://localhost:5432/");
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

app.MapPost("/api/public/syntheticPDFs/ping", async (
    IHttpClientFactory factory,
    PingRequest request,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
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
