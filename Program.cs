using SuperRecruiter;
using SuperRecruiter.Services;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddHttpClient<IWowProgressService, WowProgressService>();
builder.Services.AddHttpClient<IDiscordWebhookService, DiscordWebhookService>();
builder.Services.AddHttpClient<IRaiderIOService, RaiderIOService>();
builder.Services.AddHttpClient<IWarcraftLogsService, WarcraftLogsService>();

// Register database service
builder.Services.AddSingleton<IPlayerDatabaseService, PlayerDatabaseService>();

// Register the worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Initialize database
var dbService = host.Services.GetRequiredService<IPlayerDatabaseService>();
await dbService.InitializeDatabaseAsync();

host.Run();
