using SuperRecruiter;
using SuperRecruiter.Services;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddSingleton<IWowProgressService, WowProgressService>();
builder.Services.AddHttpClient<IDiscordWebhookService, DiscordWebhookService>();

// Register the worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
