using Discord.Interactions;
using Discord.WebSocket;
using Discord;

[RequireContext(ContextType.Guild)]
public class CommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxChars = 2000;
    private readonly HttpClient _client = new();
    private readonly ulong _channelId = ulong.Parse(Environment.GetEnvironmentVariable("ALLOWED_CHANNEL_ID")
        ?? throw new ArgumentNullException("ALLOWED_CHANNEL_ID", "Allowed channel id is not set."));
    private readonly SqliteService dbService;
    private readonly DiscordSocketClient discordSocketClient;

    private static bool handlingModals = false;

    public CommandModule(SqliteService dbService, DiscordSocketClient discordSocketClient) : base()
    {
        this.discordSocketClient = discordSocketClient;
        if (!handlingModals)
        {
            discordSocketClient.ModalSubmitted += this.HandleModalSubmittedAsync;
        }
        handlingModals = true;
        this.dbService = dbService;
    }

    [SlashCommand("add-quote", description: "adds a quote", runMode: RunMode.Async)]
    public async Task HandleAddQuoteCommandAsync()
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }


        await RespondWithModalAsync(Messages.CreateAddQuoteModal());
    }

    public async Task<Quote> AddQuoteAsync(string name, string quote, string culprit, DateTime dateTime) {

        Quote quoteEntity;
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
        return quoteEntity;
    }

    [SlashCommand("get-quote", description: "get a quote", runMode: RunMode.Async)]
    public async Task GetQuoteAsync([Summary("name", "the name of the quote"), Autocomplete(typeof(QuoteNameAutoCompleter))] string name)
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
        await ReplyWithEmbedAsync(quote);
    }

    [SlashCommand("list-quotes", description: "list all quotes", runMode: RunMode.Async)]
    public async Task ListQuotesAsync([Summary("culprit", "the name of the culprit"), Autocomplete(typeof(QuoteCulpritAutoCompleter))] string? culprit = null, [Choice("Sort by date", (int)SortType.Date), Choice("Sort by upvotes", (int)SortType.Upvotes)] int sortType = (int)SortType.Upvotes)
    {
        SortType sortTypeEnumVal = (SortType)sortType;

        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        await DeferAsync().ConfigureAwait(false);
        List<string> quotes = await dbService.GetAllQuotesAsync(sortTypeEnumVal, culprit).ConfigureAwait(false);

        await ReplyWithinCharacterLimit($"**{quotes.Count} Quotes:**\n" + string.Join("\n", quotes)).ConfigureAwait(false);
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
        await ReplyWithEmbedAsync(quote).ConfigureAwait(false);
    }


    [SlashCommand("delete-quote", description: "delete a quote", runMode: RunMode.Async)]
    public async Task DeleteQuote([Summary("name", "the name of the quote"), Autocomplete(typeof(QuoteNameAutoCompleter))] string name)
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

    [SlashCommand("edit-quote", description: "edit date and/or content and/or culprit and/or date of a quote", runMode: RunMode.Async)]
    public async Task HandleEditQuoteCommandAsync([Summary("name", "current name of the quote"), Autocomplete(typeof(QuoteNameAutoCompleter))] string name)
    {
        if (!IsChannelAllowed(Context.Channel))
        {
            await RespondAsync("This text channel is not allowed.").ConfigureAwait(false);
            return;
        }
        Quote? quote = await dbService.GetQuoteAsync(name);
        if (quote == null)
        {
            await FollowupAsync("The Quote does not exist.").ConfigureAwait(false);
            return;
        }

        await RespondWithModalAsync(Messages.CreateEditQuoteModal((Quote)quote));
    }

    public async Task<Quote> EditQuoteAsync(string name, string? newName, string? newQuote, string? newCulprit, DateTime? newCreatedAt) {
        await dbService.EditQuoteAsync(name, newName, newQuote, newCulprit, newCreatedAt);
        Quote? quote = await dbService.GetQuoteAsync(newName != null ? newName : name);
        return (Quote)quote;
    }


    [SlashCommand("add-audio", description: "add audio to a quote", runMode: RunMode.Async)]
    public async Task AddAudioAsync([Summary("name", "the name of the quote"), Autocomplete(typeof(QuoteNameAutoCompleter))] string name, [Summary("audio", "audio proof")] IAttachment attachment)
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
        await ReplyWithEmbedAsync(quote, "Audio attached.");
    }

    [SlashCommand("remove-audio", description: "remove audio from a quote", runMode: RunMode.Async)]
    public async Task RemoveAudioAsync([Summary("name", "the name of the quote"), Autocomplete(typeof(QuoteNameAutoCompleter))] string name)
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
        await ReplyWithEmbedAsync(quote, "Audio removed.");
    }

    private async Task ReplyWithEmbedAsync(Quote quote, string message = "", SocketModal? modal = null)
    {
        Embed embed = Messages.CreateEmbed(quote.Name, quote.Content, quote.Culprit, quote.CreatedAt, quote.Upvotes);
        MessageComponent upvoteComponent = Messages.CreateQuoteButtonComponent(quote.Name);
        if (!String.IsNullOrEmpty(quote.FilePath))
        {
            await using var fs = new FileStream(quote.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var info = new FileInfo(quote.FilePath);
            if (modal == null)
            {
                await FollowupWithFileAsync(fs, Util.ReplaceInvalidChars($"{quote.Name} - {quote.Culprit}{info.Extension}"), text: message, embed: embed, components: upvoteComponent).ConfigureAwait(false);
            }
            else
            {
                await modal.RespondWithFileAsync(fs, Util.ReplaceInvalidChars($"{quote.Name} - {quote.Culprit}{info.Extension}"), text: message, embed: embed, components: upvoteComponent);
            }
            return;
        }
        if (modal == null)
        {
            await FollowupAsync(message, embed: embed, components: upvoteComponent).ConfigureAwait(false);
        }
        else
        {
            await modal.RespondAsync(message, embed: embed, components: upvoteComponent).ConfigureAwait(false);
        }
    }

    private async Task<bool> QuoteExists(string name)
    {
        return (await dbService.GetQuoteAsync(name)) != null;
    }

    private bool IsChannelAllowed(IChannel channel)
    {
        return channel.Id == _channelId;
    }

    private async Task ReplyWithinCharacterLimit(string message)
    {
        if(message.Length <= MaxChars)
        {
            await FollowupAsync(message).ConfigureAwait(false);
            return;
        }

        var messageLines = message.Split("\n");
        string messagePart = "";
        bool first = true;
        foreach (var line in messageLines)
        {
            if (messagePart.Length + line.Length + 1 <= MaxChars)
            {
                messagePart += line + "\n";
                continue;
            }

            if (first)
            {
                await FollowupAsync(messagePart).ConfigureAwait(false);
                first = false;
            }
            else
            {
                await Context.Channel.SendMessageAsync(messagePart).ConfigureAwait(false);
            }
            messagePart = line + "\n";
        }
        if (!string.IsNullOrEmpty(messagePart))
        {
            if (first)
            {
                await FollowupAsync(messagePart).ConfigureAwait(false);
                return;
            }
            await Context.Channel.SendMessageAsync(messagePart).ConfigureAwait(false);
        }
    }

    public async Task HandleModalSubmittedAsync(SocketModal modal) {
        string nameOrNewName = "";
        string quote = "";
        string culprit = "";
        string date = "";
        foreach(SocketMessageComponentData component in modal.Data.Components) {
            switch (component.CustomId) {
                case "name": nameOrNewName = component.Value.Trim(); break;
                case "quote": quote = component.Value.Trim(); break;
                case "culprit": culprit = component.Value.Trim(); break;
                case "date": date = component.Value.Trim(); break;
            }
        }

        DateTime dateTime = new DateTime();
        if (!string.IsNullOrWhiteSpace(date) && !DateTime.TryParseExact(date, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out dateTime))
        {
            await modal.RespondAsync("Date is not in the correct format. Format is: dd.MM.yyyy");
            return;
        }

        if (string.IsNullOrWhiteSpace(nameOrNewName))
        {
            await modal.RespondAsync("Please provide a name.").ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(quote))
        {
            await modal.RespondAsync("Please provide a quote text.").ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(culprit))
        {
            await modal.RespondAsync("Please provide a culprit.").ConfigureAwait(false);
            return;
        }

        if (modal.Data.CustomId == Messages.ADD_QUOTE_MODAL_CUSTOMID)
        {
            if (await QuoteExists(nameOrNewName))
            {
                await modal.RespondAsync("A quote with this name already exists.").ConfigureAwait(false);
                return;
            }
            Quote finalQuoteEntity;
            finalQuoteEntity = await AddQuoteAsync(nameOrNewName, quote, culprit, string.IsNullOrWhiteSpace(date) ? DateTime.Now : dateTime);
            await ReplyWithEmbedAsync(finalQuoteEntity, "Quote added.", modal:modal);
        }
        else if (modal.Data.CustomId.StartsWith(Messages.EDIT_QUOTE_MODAL_PREFIX))
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                await modal.RespondAsync("The Quote needs a date.").ConfigureAwait(false);
                return;
            }
            String oldName = modal.Data.CustomId.Substring(Messages.EDIT_QUOTE_MODAL_PREFIX.Length);
            if (nameOrNewName != oldName && await QuoteExists(nameOrNewName))
            {
                await modal.RespondAsync("A quote with this name already exists.").ConfigureAwait(false);
                return;
            }
            Quote finalQuoteEntity;
            finalQuoteEntity = await EditQuoteAsync(oldName,
                    string.IsNullOrWhiteSpace(nameOrNewName) || nameOrNewName == oldName ? null : nameOrNewName,
                     quote,
                     culprit,
                     dateTime);
            await ReplyWithEmbedAsync(finalQuoteEntity, "Quote edited.", modal:modal);
        }
    }
}
