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
    private readonly LeaderboardService _leaderboardService;

    public DiscordClientHost(
        DiscordSocketClient discordSocketClient,
        InteractionService interactionService,
        IServiceProvider serviceProvider,
        ILogger<DiscordClientHost> logger,
        SqliteService dbService,
        LeaderboardService leaderboardService)
    {
        ArgumentNullException.ThrowIfNull(discordSocketClient);
        ArgumentNullException.ThrowIfNull(interactionService);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dbService);
        ArgumentNullException.ThrowIfNull(leaderboardService);

        _discordSocketClient = discordSocketClient;
        _interactionService = interactionService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dbService = dbService;
        _leaderboardService = leaderboardService;
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

    private async Task<IResult> InteractionCreated(SocketInteraction interaction)
    {
        var interactionContext = new SocketInteractionContext(_discordSocketClient, interaction);
        IResult res = await _interactionService.ExecuteCommandAsync(interactionContext, _serviceProvider);
        Task.Run(async () =>
        {
            await Task.Delay(3000);
            await _leaderboardService.UpdateLeaderboardAsync();
        });
        return res;
    }

    private async Task ButtonExecutedAsync(SocketMessageComponent component)
    {
        string quoteName = "";
        string userId = component.User.Id.ToString();

        if (component.Data.CustomId.StartsWith(Messages.ADD_UPVOTE_BUTTON_PREFIX))
        {
            quoteName = component.Data.CustomId.Substring(Messages.ADD_UPVOTE_BUTTON_PREFIX.Length);
            if (await _dbService.HasUserUpvotedAsync(userId, quoteName))
            {
                await component.RespondAsync("Already upvoted this quote.", ephemeral: true);
                return;
            }
            await _dbService.UpvoteQuoteAsync(userId, quoteName);

            await component.RespondAsync("Upvoted quote.", ephemeral: true);
        }
        else if (component.Data.CustomId.StartsWith(Messages.REMOVE_UPVOTE_BUTTON_PREFIX))
        {
            quoteName = component.Data.CustomId.Substring(Messages.REMOVE_UPVOTE_BUTTON_PREFIX.Length);
            if (!await _dbService.HasUserUpvotedAsync(userId, quoteName))
            {
                await component.RespondAsync("You did not upvote this quote.", ephemeral: true);
                return;
            }
            await _dbService.RemoveUpvoteAsync(userId, quoteName);
            await component.RespondAsync("Upvote removed.", ephemeral: true);
        }
        else if (component.Data.CustomId.StartsWith(Messages.EDIT_QUOTE_BUTTON_PREFIX))
        {
            quoteName = component.Data.CustomId.Substring(Messages.EDIT_QUOTE_BUTTON_PREFIX.Length);
        }
        
        Quote? quoteOpt = await _dbService.GetQuoteAsync(quoteName);
        if (quoteOpt == null)
            return;
        Quote quote = (Quote)quoteOpt;
        if (component.Data.CustomId.StartsWith(Messages.EDIT_QUOTE_BUTTON_PREFIX))
        {
            await component.RespondWithModalAsync(Messages.CreateEditQuoteModal(quote));
        }
        else
        {
            await component.Message.ModifyAsync(props =>
            {
                props.Embed = Messages.CreateEmbed(quote.Name, quote.Content, quote.Culprit, quote.CreatedAt, quote.Upvotes);
            }).ConfigureAwait(false);
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
        
        await _leaderboardService.CreateLeaderboard();
    }

}
