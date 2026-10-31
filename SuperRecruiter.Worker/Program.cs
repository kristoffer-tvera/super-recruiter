using SuperRecruiter.Worker;
using SuperRecruiter.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Our own API client
builder.Services.AddHttpClient<SuperRecruiterApiClient>(client =>
{
    var baseUrl = builder.Configuration["SuperRecruiterApi:BaseUrl"] ?? throw new InvalidOperationException("SuperRecruiterApi:BaseUrl is not configured");
    var apiKey = builder.Configuration["SuperRecruiterApi:ApiKey"] ?? throw new InvalidOperationException("SuperRecruiterApi:ApiKey is not configured");

    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

var mode = builder.Configuration["WorkerMode"] ?? "Server";
if (mode.Equals("WowProgress", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["FlareSolverrUrl"]))
        throw new InvalidOperationException("FlareSolverrUrl is required in WowProgress mode");
    builder.Services.AddHttpClient<WowProgressService>(client => client.Timeout = TimeSpan.FromSeconds(70));
    builder.Services.AddHostedService<WowProgressWorker>();
}
else if (mode.Equals("Server", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<RaiderIOService>();
    builder.Services.AddSingleton<PlayerCacheService>();
    builder.Services.AddSingleton<AdminFilterService>();
    builder.Services.AddTransient<PlayerIngestionService>();
    builder.Services.AddSingleton<DiscordBotService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());
    builder.Services.AddHostedService<ScraperWorker>();
}
else
{
    throw new InvalidOperationException("WorkerMode must be Server or WowProgress");
}

var host = builder.Build();
host.Run();
