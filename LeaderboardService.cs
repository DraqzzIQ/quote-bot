using Discord;
using Discord.WebSocket;

public class LeaderboardService(SqliteService dbService, DiscordSocketClient client)
{
    private List<Quote> _currentLeaderboard = [];
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
        _leaderboardMessage = await channel.SendFilesAsync(GetFileAttachments(_currentLeaderboard), text: GetFormattedLeaderboard(_currentLeaderboard)).ConfigureAwait(false);
        await _leaderboardMessage.PinAsync().ConfigureAwait(false);
    }

    public async Task UpdateLeaderboardAsync()
    {
        List<Quote> newLeaderboard = await dbService.GetUpvotedQuotesAsync(7).ConfigureAwait(false);
        if (newLeaderboard.Count == _currentLeaderboard.Count &&
            !newLeaderboard.Where((t, i) => !t.Equals(_currentLeaderboard[i])).Any())
            return;

        await _leaderboardMessage.ModifyAsync(props =>
        {
            props.Content = GetFormattedLeaderboard(newLeaderboard);
            props.Attachments = GetFileAttachments(newLeaderboard);
        }).ConfigureAwait(false);
        _currentLeaderboard = newLeaderboard;
    }

    private string GetFormattedLeaderboard(List<Quote> leaderboard)
    {
        return leaderboard.Aggregate("## Top Quotes\n", (current, quote) => current + $"**{quote.Upvotes}⬆️: {quote.Name} - {quote.Culprit}, {quote.CreatedAt:dd.MM.yy}**\n„{quote.Content}”\n");
    }

    private List<FileAttachment> GetFileAttachments(List<Quote> leaderboard)
    {
        List<FileAttachment> fileAttachments = [];
        foreach (Quote quote in leaderboard)
        {
            if (quote.FilePath == "")
                continue;

            var info = new FileInfo(quote.FilePath);
            var file = new FileAttachment(quote.FilePath, Util.ReplaceInvalidChars($"{quote.Name} - {quote.Culprit}{info.Extension}"));
            fileAttachments.Add(file);
        }
        return fileAttachments;
    }
}
