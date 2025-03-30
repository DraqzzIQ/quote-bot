using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Discord.Rest;
using Discord.Interactions;
using Discord.WebSocket;

var builder = new HostApplicationBuilder(args);

builder.Services.AddSingleton<DiscordSocketClient>();
// tmp fix:
builder.Services.AddSingleton<IRestClientProvider>(x => x.GetRequiredService<DiscordSocketClient>());
// end tmp fix
builder.Services.AddSingleton<InteractionService>();
builder.Services.AddHostedService<DiscordClientHost>();
builder.Services.AddSingleton<SqliteService>();

// Logging
#if DEBUG
builder.Services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
#else
builder.Services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information));
#endif

var app = builder.Build();
app.Run();
