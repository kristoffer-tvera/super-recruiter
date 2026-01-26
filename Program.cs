using SuperRecruiter;
using SuperRecruiter.Services;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddHttpClient<IWowProgressService, WowProgressService>();
builder.Services.AddHttpClient<IDiscordWebhookService, DiscordWebhookService>();
builder.Services.AddHttpClient<IRaiderIOService, RaiderIOService>();
builder.Services.AddHttpClient<IWarcraftLogsService, WarcraftLogsService>();

// Register the worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
