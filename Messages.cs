using Discord;

public class Messages
{

    public static readonly string EDIT_QUOTE_MODAL_PREFIX = "edit_quote:";
    public static readonly string ADD_QUOTE_MODAL_CUSTOMID = "add_quote";
    public static readonly string ADD_UPVOTE_BUTTON_PREFIX = "ADD:";
    public static readonly string REMOVE_UPVOTE_BUTTON_PREFIX = "REMOVE:";
    public static readonly string EDIT_QUOTE_BUTTON_PREFIX = "EDIT:";

    public static Embed CreateEmbed(string name, string quote, string author, DateTime date)
    {
        var builder = new EmbedBuilder();
        return builder.AddField($"{name}", $"„{quote}”")
        .WithFooter($"{author} - {date.ToString("dd.MM.yyyy")}").Build();
    }

    public static MessageComponent CreateQuoteButtonComponent(string quoteName, int upvotes)
    {
        return new ComponentBuilder().WithButton(label: "Upvote (" + upvotes + ")", customId: ADD_UPVOTE_BUTTON_PREFIX + quoteName).WithButton(label: "Remove Upvote", customId: REMOVE_UPVOTE_BUTTON_PREFIX + quoteName).WithButton("Edit", customId: EDIT_QUOTE_BUTTON_PREFIX + quoteName).Build();
    }

    public static Modal CreateEditQuoteModal(Quote oldQuote)
    {
        var mb = new ModalBuilder()
            .WithTitle("Edit Quote \"" + oldQuote.Name + "\"")
            .WithCustomId(EDIT_QUOTE_MODAL_PREFIX + oldQuote.Name)
            .AddTextInput("Name", "name", placeholder: oldQuote.Name, value: oldQuote.Name)
            .AddTextInput("Culprit", "culprit", placeholder: oldQuote.Culprit, value: oldQuote.Culprit)
            .AddTextInput("Quote", "quote", placeholder: oldQuote.Content, value: oldQuote.Content)
            .AddTextInput("Date", "date", placeholder: oldQuote.CreatedAt.Date.ToString("dd.MM.yyyy"), value: oldQuote.CreatedAt.Date.ToString("dd.MM.yyyy"));
        return mb.Build();
    }

    public static Modal CreateAddQuoteModal()
    {
        var mb = new ModalBuilder()
            .WithTitle("Add Quote")
            .WithCustomId(ADD_QUOTE_MODAL_CUSTOMID)
            .AddTextInput("Name", "name", placeholder: "what did he sayyy")
            .AddTextInput("Culprit", "culprit", placeholder: "Donald J. Trump")
            .AddTextInput("Quote", "quote", placeholder: "Smart people don't like me")
            .AddTextInput("Date", "date", placeholder: "13.09.2025", required: false);
        return mb.Build();
    }
}
