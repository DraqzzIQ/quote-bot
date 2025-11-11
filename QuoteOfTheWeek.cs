using Discord;
using Discord.WebSocket;
using Quartz;

public class QuoteOfTheWeek(DiscordSocketClient client, SqliteService dbService) : IJob
{
    private readonly ulong _channelId = ulong.Parse(Environment.GetEnvironmentVariable("ALLOWED_CHANNEL_ID")
                                                    ?? throw new ArgumentNullException("ALLOWED_CHANNEL_ID", "Allowed channel id is not set."));
    public async Task Execute(IJobExecutionContext context)
    {
        var channel = (ITextChannel)await client.GetChannelAsync(_channelId).ConfigureAwait(false);
        var quote = await dbService.GetRandomQuoteAsync(2).ConfigureAwait(false);
        if (quote == null)
            return;

        var embed = Messages.CreateEmbed(quote.Value.Name, quote.Value.Content, quote.Value.Culprit,
            quote.Value.CreatedAt, quote.Value.Upvotes);
        MessageComponent upvoteComponent = Messages.CreateQuoteButtonComponent(quote.Value.Name);

        if (quote.Value.FilePath == "")
        {
            await channel.SendMessageAsync(embed: embed, text: "## Quote of the week", components: upvoteComponent).ConfigureAwait(false);
            return;
        }

        await using var fs = new FileStream(quote.Value.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var info = new FileInfo(quote.Value.FilePath);
        await channel.SendFileAsync(fs, ReplaceInvalidChars($"{quote.Value.Name} - {quote.Value.Culprit}{info.Extension}"), text: "## Motivational quote of the week", embed: embed, components: upvoteComponent).ConfigureAwait(false);
    }

    private string ReplaceInvalidChars(string filename)
    {
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }
}
