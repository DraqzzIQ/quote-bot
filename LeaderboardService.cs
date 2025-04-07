using Discord;
using Discord.WebSocket;

public class LeaderboardService(SqliteService dbService, DiscordSocketClient client)
{
    private List<(string, string)> _currentLeaderboard = [];
    private IUserMessage _leaderboardMessage;
    private readonly ulong _channelId = ulong.Parse(Environment.GetEnvironmentVariable("SCOREBOARD_CHANNEL_ID")
        ?? throw new ArgumentNullException("SCOREBOARD_CHANNEL_ID", "Scoreboard channel id is not set."));

    public async Task CreateLeaderboard()
    {
        var channel = (ITextChannel)await client.GetChannelAsync(_channelId).ConfigureAwait(false);

        foreach (IMessage pinnedMessage in await channel.GetPinnedMessagesAsync().ConfigureAwait(false))
        {
            if (client.CurrentUser.Id != pinnedMessage.Author.Id)
                continue;

            _leaderboardMessage = (IUserMessage)pinnedMessage;
            await UpdateLeaderboardAsync();
            return;
        }

        _currentLeaderboard = await dbService.GetUpvotedQuotesAsync(7).ConfigureAwait(false);
        _leaderboardMessage = await channel.SendMessageAsync(GetFormattedLeaderboard(_currentLeaderboard)).ConfigureAwait(false);
        await _leaderboardMessage.PinAsync().ConfigureAwait(false);
    }

    public async Task UpdateLeaderboardAsync()
    {
        List<(string, string)> newLeaderboard = await dbService.GetUpvotedQuotesAsync(7).ConfigureAwait(false);
        if (string.Join("", newLeaderboard) == string.Join("", _currentLeaderboard))
            return;
        
        await _leaderboardMessage.ModifyAsync(props =>
        {
            props.Content = GetFormattedLeaderboard(newLeaderboard);
        }).ConfigureAwait(false);
        _currentLeaderboard = newLeaderboard;
    }
    
    private string GetFormattedLeaderboard(List<(string, string)> leaderboard)
    {
        string formatted = "## Top Quotes\n";
        foreach ((string header, string quote) in leaderboard)
        {
            formatted += $"**{header}**\n„{quote}”\n";
        }
        return formatted;
    }
}
