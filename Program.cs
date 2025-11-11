using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Discord.Rest;
using Discord.Interactions;
using Discord.WebSocket;
using Quartz;
using Quartz.Impl;

var builder = new HostApplicationBuilder(args);


builder.Services.AddSingleton<InteractionService>();
builder.Services.AddHostedService<DiscordClientHost>();
builder.Services.AddSingleton<SqliteService>();
builder.Services.AddSingleton<DiscordSocketClient>();
// tmp fix:
builder.Services.AddSingleton<IRestClientProvider>(x => x.GetRequiredService<DiscordSocketClient>());
// end tmp fix
builder.Services.AddSingleton<LeaderboardService>();

// Logging
#if DEBUG
builder.Services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));
#else
builder.Services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information));
#endif

// Quote of the week
builder.Services.AddQuartz(q =>
{
    var qotwJobKey = new JobKey("weeklyJob");
    var cleanUnpopularQuotesJobKey = new JobKey("cleanUnpopularQuotesJob");
    q.AddJob<QuoteOfTheWeek>(opts => opts.WithIdentity(qotwJobKey));
    q.AddJob<CleanUnpopularQuotes>(opts => opts.WithIdentity(cleanUnpopularQuotesJobKey));
    q.AddTrigger(opts => opts
        .ForJob(qotwJobKey)
        .WithIdentity("weeklyJobTrigger")
        .WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Monday, 9, 0)));
    q.AddTrigger(opts => opts
        .ForJob(cleanUnpopularQuotesJobKey)
        .WithIdentity("cleanUnpopularQuotesTrigger")
        .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(0, 0)));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

var app = builder.Build();
app.Run();
