using SuperRecruiter.Worker;
using SuperRecruiter.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// External API clients
builder.Services.AddHttpClient<WowProgressService>();
builder.Services.AddHttpClient<RaiderIOService>();
builder.Services.AddHttpClient<WarcraftLogsService>();

// Our own API client
builder.Services.AddHttpClient<SuperRecruiterApiClient>(client =>
{
    var baseUrl = builder.Configuration["SuperRecruiterApi:BaseUrl"] ?? throw new InvalidOperationException("SuperRecruiterApi:BaseUrl is not configured");
    var apiKey = builder.Configuration["SuperRecruiterApi:ApiKey"] ?? throw new InvalidOperationException("SuperRecruiterApi:ApiKey is not configured");

    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

// Player cache (refreshed each scan cycle)
builder.Services.AddSingleton<PlayerCacheService>();

// Discord bot (singleton — maintains gateway connection)
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());

// Scraper worker
builder.Services.AddHostedService<ScraperWorker>();

var host = builder.Build();
host.Run();
