using super_recruiter;
using super_recruiter.Services;

var builder = Host.CreateApplicationBuilder(args);

// Register HttpClient with HTTP/1.1 (like REST Client uses)
builder
    .Services.AddHttpClient<IWowProgressService, WowProgressService>()
    .ConfigurePrimaryHttpMessageHandler(() =>
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            // Force HTTP/1.1 - some sites block HTTP/2
            EnableMultipleHttp2Connections = false,
        }
    )
    .ConfigureHttpClient(client =>
    {
        // Force HTTP 1.1 like REST Client uses
        client.DefaultRequestVersion = new Version(1, 1);
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    });

builder.Services.AddHttpClient<IDiscordWebhookService, DiscordWebhookService>();

// Register the worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
