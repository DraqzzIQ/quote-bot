using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class DiscordClientHost : IHostedService
{
    private readonly DiscordSocketClient _discordSocketClient;
    private readonly InteractionService _interactionService;
    private readonly ILogger<DiscordClientHost> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SqliteService _dbService;
    private IUserMessage? leaderboardMessage = null;

    private List<string> currentLeaderboard;

    public DiscordClientHost(
        DiscordSocketClient discordSocketClient,
        InteractionService interactionService,
        IServiceProvider serviceProvider,
        ILogger<DiscordClientHost> logger,
        SqliteService dbService)
    {
        ArgumentNullException.ThrowIfNull(discordSocketClient);
        ArgumentNullException.ThrowIfNull(interactionService);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _discordSocketClient = discordSocketClient;
        _interactionService = interactionService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dbService = dbService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _discordSocketClient.InteractionCreated += InteractionCreated;
        _discordSocketClient.Ready += ClientReady;
        _discordSocketClient.Log += LogAsync;
        _discordSocketClient.ButtonExecuted += ButtonExecutedAsync;

        await _discordSocketClient
            .LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BOT_TOKEN") ?? throw new ArgumentNullException("BOT_TOKEN", "Bot token is not set."))
            .ConfigureAwait(false);

        await _discordSocketClient
            .StartAsync()
            .ConfigureAwait(false);
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _discordSocketClient.InteractionCreated -= InteractionCreated;
        _discordSocketClient.Ready -= ClientReady;
        _discordSocketClient.Log -= LogAsync;

        await _discordSocketClient
            .StopAsync()
            .ConfigureAwait(false);
    }

    private Task LogAsync(LogMessage arg)
    {
        if (arg.Exception is InteractionException interactionException)
        {
            _logger.LogError(interactionException,
                $"{interactionException.GetBaseException().GetType()} was thrown while executing {interactionException.CommandInfo} in Channel {interactionException.InteractionContext.Channel} on Server {interactionException.InteractionContext.Guild} by user {interactionException.InteractionContext.User}.");
            return Task.CompletedTask;
        }

        switch (arg.Severity)
        {
            case LogSeverity.Critical:
                _logger.LogCritical(arg.Exception, arg.Message);
                break;
            case LogSeverity.Error:
                _logger.LogError(arg.Exception, arg.Message);
                break;
            case LogSeverity.Warning:
                _logger.LogWarning(arg.Exception, arg.Message);
                break;
            case LogSeverity.Info:
                _logger.LogInformation(arg.Exception, arg.Message);
                break;
            case LogSeverity.Verbose:
                _logger.LogTrace(arg.Exception, arg.Message);
                break;
            case LogSeverity.Debug:
                _logger.LogDebug(arg.Exception, arg.Message);
                break;
        }

        return Task.CompletedTask;
    }

    private Task<IResult> InteractionCreated(SocketInteraction interaction)
    {
        var interactionContext = new SocketInteractionContext(_discordSocketClient, interaction);
        Task<IResult> res = _interactionService.ExecuteCommandAsync(interactionContext, _serviceProvider);
        Task.Run(async () =>
        {
            await res;
            Task<List<string>> dbTask = _dbService.GetAllQuotesAsync(SortType.Upvotes, maxCount: 7);
            if (leaderboardMessage == null)
            {
                leaderboardMessage = await interaction.Channel.SendMessageAsync("**Top Quotes**");
                await leaderboardMessage.PinAsync();
            }
            else
            {
                bool isDifferent = false;
                List<string> leaderboard = await dbTask;
                for (int i = 0; i < leaderboard.Count(); i++)
                {
                    if (!Object.Equals(leaderboard[i], currentLeaderboard[i]))
                    {
                        isDifferent = true;
                    }
                }
                if (!isDifferent)
                {
                    return;
                }
            }


            await leaderboardMessage.ModifyAsync(async props =>
            {
                props.Content = Optional.Create<string>("**Top Quotes**\n" + string.Join("\n", await dbTask));
            }).ConfigureAwait(false);
        });
        return res;
    }

    private async Task ButtonExecutedAsync(SocketMessageComponent component)
    {
        string quoteName = component.Data.CustomId.Split(':')[1];
        string userId = component.User.Id.ToString();

        if (component.Data.CustomId.StartsWith("ADD"))
        {
            if (await _dbService.HasUserUpvotedAsync(userId, quoteName))
            {
                await component.RespondAsync("Already upvoted this quote.", ephemeral: true);
                return;
            }
            await _dbService.UpvoteQuoteAsync(userId, quoteName);

            await component.RespondAsync("Upvoted quote.", ephemeral: true);
        }
        else if (component.Data.CustomId.StartsWith("REMOVE"))
        {
            await _dbService.RemoveUpvoteAsync(userId, quoteName);
            await component.RespondAsync("Upvote removed.", ephemeral: true);
        }
    }

    private async Task ClientReady()
    {
        await _interactionService
            .AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider)
            .ConfigureAwait(false);

        // enable logging
        _interactionService.Log += LogAsync;

        // set activity
        await _discordSocketClient
            .SetActivityAsync(new Game("Reading Quotes", ActivityType.CustomStatus))
            .ConfigureAwait(false);

        await _discordSocketClient
            .SetCustomStatusAsync("Dying of shame")
            .ConfigureAwait(false);

        // register commands to guild
        await _interactionService
            .RegisterCommandsToGuildAsync(ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID") ?? throw new ArgumentNullException("GUILD_ID", "Guild id is not set.")))
            .ConfigureAwait(false);
    }

}
