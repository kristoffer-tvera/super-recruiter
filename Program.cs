using SuperRecruiter;
using SuperRecruiter.Services;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddHttpClient<WowProgressService>();
builder.Services.AddHttpClient<DiscordWebhookService>();
builder.Services.AddHttpClient<RaiderIOService>();
builder.Services.AddHttpClient<WarcraftLogsService>();

// Register database service
builder.Services.AddSingleton<PlayerDatabaseService>();

// Register the worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Initialize database
var dbService = host.Services.GetRequiredService<PlayerDatabaseService>();
await dbService.InitializeDatabaseAsync();

host.Run();
