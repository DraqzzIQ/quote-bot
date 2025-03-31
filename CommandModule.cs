using Discord.Interactions;
using Discord;

[RequireContext(ContextType.Guild)]
public class CommandModule(SqliteService dbService) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly HttpClient _client = new();
    private readonly ulong _channelId = ulong.Parse(Environment.GetEnvironmentVariable("ALLOWED_CHANNEL_ID")
        ?? throw new ArgumentNullException("ALLOWED_CHANNEL_ID", "Allowed channel id is not set."));

    [SlashCommand("add-quote", description: "adds a quote", runMode: RunMode.Async)]
    public async Task AddQuoteAsync([Summary("name", "the name of the quote")] string name,
        [Summary("culprit", "the one quoted")] string culprit,
        [Summary("quote", "the quote")] string quote,
        [Summary("audio", "optional audio proof")] IAttachment? attachment = null,
        [Summary("date", "optionally supply date of quote in format: dd.MM.yy")] string date = "")
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrWhiteSpace(quote))
        {
            await RespondAsync("Please provide a quote text.").ConfigureAwait(false);
            return;
        }
        DateTime dateTime;
        if (string.IsNullOrWhiteSpace(date))
        {
            dateTime = DateTime.Now;
        }
        else if (!DateTime.TryParseExact(date, "dd.MM.yy", null, System.Globalization.DateTimeStyles.None, out dateTime))
        {
            await RespondAsync("Date is not in the correct format. Format is: dd.MM.yy");
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        if (await QuoteExists(name))
        {
            await FollowupAsync("A quote with this name already exists.").ConfigureAwait(false);
            return;
        }

        Quote quoteEntity;
        Embed embed = Messages.CreateEmbed(name, quote!, culprit, dateTime, 0);
        MessageComponent upvoteComponent = Messages.CreateVoteComponent(name);
        if (attachment == null)
        {
            quoteEntity = new Quote
            {
                Name = name,
                Culprit = culprit,
                Content = quote,
                FilePath = "",
                Upvotes = 0,
                CreatedAt = dateTime
            };
            await dbService.AddQuoteAsync(quoteEntity);
            await FollowupAsync(text: "Added quote", embed: embed, components: upvoteComponent).ConfigureAwait(false);
            return;
        }

        if (!attachment.ContentType.StartsWith("audio"))
        {
            await RespondAsync("Attachment can only be audio.").ConfigureAwait(false);
            return;
        }

        var info = new FileInfo(attachment.Filename);
        string filePath = "/bot-data/quotes/" + Guid.NewGuid().ToString() + info.Extension;
        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var response = await _client.GetAsync(attachment!.Url).ConfigureAwait(false);
        await response.Content.CopyToAsync(fs).ConfigureAwait(false);
        await fs.DisposeAsync();

        quoteEntity = new Quote
        {
            Content = quote,
            FilePath = filePath,
            Name = name,
            Culprit = culprit,
            Upvotes = 0,
            CreatedAt = dateTime
        };

        await dbService.AddQuoteAsync(quoteEntity);

        await FollowUpWithEmbedAsync(quoteEntity, "Added quote.");
    }

    [SlashCommand("get-quote", description: "get a quote", runMode: RunMode.Async)]
    public async Task GetQuoteAsync([Summary("name", "the name of the quote")] string name)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync();
        if (!await QuoteExists(name))
        {
            await FollowupAsync("The Quote does not exist.").ConfigureAwait(false);
            return;
        }

        Quote? quoteOpt = await dbService.GetQuoteAsync(name).ConfigureAwait(false);
        if (quoteOpt == null)
        {
            await FollowupAsync("Couldn't find quote with that name").ConfigureAwait(false);
            return;
        }

        Quote quote = (Quote)quoteOpt;
        await FollowUpWithEmbedAsync(quote);
    }

    [SlashCommand("list-quotes", description: "list all quotes", runMode: RunMode.Async)]
    public async Task ListQuotesAsync([Summary("culprit", "the name of the culprit")] string? culprit = null, [Choice("Sort by date", (int)SortType.Date), Choice("Sort by upvotes", (int)SortType.Upvotes)] int sortType = (int)SortType.Upvotes)
    {
        SortType sortTypeEnumVal = (SortType)sortType;

        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync().ConfigureAwait(false);
        List<string> quotes = await dbService.GetAllQuotesAsync(sortTypeEnumVal, culprit).ConfigureAwait(false);

        await FollowupAsync("**Quotes:**\n" + string.Join("\n", quotes)).ConfigureAwait(false);
    }

    [SlashCommand("random-quote", description: "get a random quote", runMode: RunMode.Async)]
    public async Task GetRandomQuote()
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync().ConfigureAwait(false);

        Quote? quoteOpt = await dbService.GetRandomQuoteAsync().ConfigureAwait(false);
        if (quoteOpt == null)
        {
            await FollowupAsync("Couldn't find any quote.").ConfigureAwait(false);
            return;
        }

        Quote quote = (Quote)quoteOpt;
        await FollowUpWithEmbedAsync(quote).ConfigureAwait(false);
    }


    [SlashCommand("delete-quote", description: "delete a quote", runMode: RunMode.Async)]
    public async Task DeleteQuote([Summary("name", "the name of the quote")] string name)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        if (!await QuoteExists(name))
        {
            await RespondAsync("The Quote does not exist.").ConfigureAwait(false);
        }
        await DeferAsync().ConfigureAwait(false);
        Quote quote = (Quote)await dbService.GetQuoteAsync(name);
        await dbService.DeleteQuoteAsync(name).ConfigureAwait(false);

        if (File.Exists(quote.FilePath))
            File.Delete(quote.FilePath);

        await FollowupAsync("Quote deleted.");
    }

    [SlashCommand("edit-quote", description: "edit a quote", runMode: RunMode.Async)]
    public async Task EditQuoteAsync([Summary("name", "the name of the quote")] string name, [Summary("newQuote", "the new quote")] string newQuote)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync().ConfigureAwait(false);
        if (!await QuoteExists(name))
        {
            await FollowupAsync("The Quote does not exist.").ConfigureAwait(false);
            return;
        }

        await dbService.EditQuoteAsync(name, newQuote).ConfigureAwait(false);

        Quote quote = (Quote)await dbService.GetQuoteAsync(name);
        await FollowUpWithEmbedAsync(quote, "Quote edited.");
    }

    [SlashCommand("rename-quote", description: "rename a quote", runMode: RunMode.Async)]
    public async Task RenameQuoteAsync([Summary("name", "the original name of the quote")] string name, [Summary("newName", "the new name of the quote")] string newName)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync().ConfigureAwait(false);
        if (!await QuoteExists(name))
        {
            await FollowupAsync("The Quote does not exist.").ConfigureAwait(false);
            return;
        }
        if (await QuoteExists(newName))
        {
            await FollowupAsync("A quote with this name already exists.").ConfigureAwait(false);
            return;
        }

        await dbService.RenameQuoteAsync(name, newName).ConfigureAwait(false);

        Quote quote = (Quote)await dbService.GetQuoteAsync(newName);
        await FollowUpWithEmbedAsync(quote, "Quote renamed.");
    }

    [SlashCommand("add-audio", description: "add audio to a quote", runMode: RunMode.Async)]
    public async Task AddAudioAsync([Summary("name", "the name of the quote")] string name, [Summary("audio", "audio proof")] IAttachment attachment)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync().ConfigureAwait(false);
        if (!await QuoteExists(name))
        {
            await FollowupAsync("The Quote does not exist.").ConfigureAwait(false);
            return;
        }

        if (!attachment.ContentType.StartsWith("audio"))
        {
            Console.WriteLine(attachment.ContentType);
            await FollowupAsync("Attachment can only be audio.").ConfigureAwait(false);
            return;
        }

        var info = new FileInfo(attachment.Filename);
        string filePath = "/bot-data/quotes/" + Guid.NewGuid().ToString() + info.Extension;
        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var response = await _client.GetAsync(attachment!.Url).ConfigureAwait(false);
        await response.Content.CopyToAsync(fs).ConfigureAwait(false);
        await fs.DisposeAsync();

        await dbService.AttachMediaAsync(name, filePath).ConfigureAwait(false);

        Quote quote = (Quote)await dbService.GetQuoteAsync(name);
        await FollowUpWithEmbedAsync(quote, "Audio attached.");
    }

    [SlashCommand("remove-audio", description: "remove audio from a quote", runMode: RunMode.Async)]
    public async Task RemoveAudioAsync([Summary("name", "the name of the quote")] string name)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync().ConfigureAwait(false);
        if (!await QuoteExists(name))
        {
            await FollowupAsync("The Quote does not exist.").ConfigureAwait(false);
            return;
        }

        await dbService.RemoveMediaAsync(name).ConfigureAwait(false);

        Quote quote = (Quote)await dbService.GetQuoteAsync(name);
        await FollowUpWithEmbedAsync(quote, "Audio removed.");
    }

    [SlashCommand("update-quote-date", description: "update the date of the quote", runMode: RunMode.Async)]
    public async Task UpdateQuoteDateAsync([Summary("name", "the name of the quote")] string name, [Summary("date", "format is: dd.MM.yy")] string date)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        if (!DateTime.TryParseExact(date, "dd.MM.yy", null, System.Globalization.DateTimeStyles.None, out DateTime dateTime))
        {
            await RespondAsync("Date is not in the correct format. Format is: dd.MM.yy");
            return;
        }
        await DeferAsync().ConfigureAwait(false);
        if (!await QuoteExists(name))
        {
            await FollowupAsync("The Quote does not exist.").ConfigureAwait(false);
            return;
        }

        Quote quote = (Quote)await dbService.GetQuoteAsync(name);
        quote.CreatedAt = dateTime;

        await dbService.SetQuoteCreationDateAsync(name, dateTime);

        await FollowUpWithEmbedAsync(quote, "Date updated.");
    }

    private async Task FollowUpWithEmbedAsync(Quote quote, string message = "")
    {
        Embed embed = Messages.CreateEmbed(quote.Name, quote.Content, quote.Culprit, quote.CreatedAt, quote.Upvotes);
        MessageComponent upvoteComponent = Messages.CreateVoteComponent(quote.Name);
        if (!String.IsNullOrEmpty(quote.FilePath))
        {
            await using var fs = new FileStream(quote.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
            var info = new FileInfo(quote.FilePath);
            await FollowupWithFileAsync(fs, ReplaceInvalidChars($"{quote.Name} - {quote.Culprit}{info.Extension}"), text: message, embed: embed, components: upvoteComponent).ConfigureAwait(false);
            await fs.DisposeAsync();
            return;
        }
        await FollowupAsync(message, embed: embed, components: upvoteComponent).ConfigureAwait(false);
    }

    private async Task<bool> QuoteExists(string name)
    {
        return (await dbService.GetQuoteAsync(name)) != null;
    }

    private bool IsChannelAllowed(IChannel channel)
    {
        return channel.Id == _channelId;
    }

    private string ReplaceInvalidChars(string filename)
    {
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }
}
