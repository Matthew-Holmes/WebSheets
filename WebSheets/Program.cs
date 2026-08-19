using Amazon.S3;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using WebSheets.Components;
using WebSheets.Configuration;
using WebSheets.Services;

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

app.Run();
