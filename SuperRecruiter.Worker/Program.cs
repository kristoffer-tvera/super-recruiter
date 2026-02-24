using SuperRecruiter.Worker;
using SuperRecruiter.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// External API clients
builder.Services.AddHttpClient<GeminiService>();
builder.Services.AddHttpClient<WowProgressService>();
builder.Services.AddHttpClient<RaiderIOService>();
builder.Services.AddHttpClient<WarcraftLogsService>();

// Our own API client
builder.Services.AddHttpClient<SuperRecruiterApiClient>(client =>
{
    var baseUrl = builder.Configuration["SuperRecruiterApi:BaseUrl"] ?? "http://localhost:5100";
    client.BaseAddress = new Uri(baseUrl);
});

// Discord bot (singleton — maintains gateway connection)
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());

// Scraper worker
builder.Services.AddHostedService<ScraperWorker>();

var host = builder.Build();
host.Run();
