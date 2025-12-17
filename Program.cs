using super_recruiter;
using super_recruiter.Services;

var builder = Host.CreateApplicationBuilder(args);

// Register HttpClient for WowProgressService with custom configuration
builder
    .Services.AddHttpClient<IWowProgressService, WowProgressService>()
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler
        {
            AutomaticDecompression =
                System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            UseCookies = true,
            AllowAutoRedirect = true,
        }
    );

// Register HttpClient for DiscordWebhookService
builder.Services.AddHttpClient<IDiscordWebhookService, DiscordWebhookService>();

// Register the worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
