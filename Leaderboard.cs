using Discord;
using Discord.WebSocket;

public class Leaderboard
{
    public List<string> currentLeaderboard { get; private set;}
    private IUserMessage leaderboardMessage;
    private SqliteService _dbService;

    private readonly ulong _channelId = ulong.Parse(Environment.GetEnvironmentVariable("ALLOWED_CHANNEL_ID")
        ?? throw new ArgumentNullException("ALLOWED_CHANNEL_ID", "Allowed channel id is not set."));

    private ITextChannel _channel;

    public async Task CreateLeaderboard(SqliteService dbService, DiscordSocketClient client) {
        _channel = (ITextChannel)await client.GetChannelAsync(_channelId);
        _dbService = dbService;

        foreach (IMessage pinnedMessage in await _channel.GetPinnedMessagesAsync())
        {
            if(client.CurrentUser.Id == pinnedMessage.Author.Id) {
                leaderboardMessage = (IUserMessage) pinnedMessage;
                await UpdateLeaderboardAsync();
                return;
            }
        }
        leaderboardMessage = await _channel.SendMessageAsync("**Top Quotes**" + string.Join("\n", currentLeaderboard = await _dbService.GetAllQuotesAsync(SortType.Upvotes, maxCount: 7)));
        await leaderboardMessage.PinAsync();
    }

    public async Task UpdateLeaderboardAsync()
    {
        List<string> newLeaderboard;
        newLeaderboard = await _dbService.GetAllQuotesAsync(SortType.Upvotes, maxCount: 7);

        bool isDifferent = false;
        for (int i = 0; i < currentLeaderboard.Count(); i++)
        {
            isDifferent = currentLeaderboard[i] != newLeaderboard[i];
        }


        if (isDifferent) {
            await leaderboardMessage.ModifyAsync( props =>
            {
                props.Content = Optional.Create<string>("**Top Quotes**\n" + string.Join("\n", newLeaderboard));
            }).ConfigureAwait(false);
            currentLeaderboard = newLeaderboard;
        }
    }

}
