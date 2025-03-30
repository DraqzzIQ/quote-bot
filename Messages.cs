using Discord;

public class Messages
{
    public static Embed CreateEmbed(string name, string quote, string author, DateTime date, int upvotes)
    {
        var builder = new EmbedBuilder();
        return builder.AddField($"{upvotes}: {name}", quote)
        .WithFooter($"{author} - {date.ToString("dd.MM.yy")}").Build();
    }

    public static MessageComponent CreateVoteComponent(string quoteName)
    {
        return new ComponentBuilder().WithButton(label: "Upvote", customId: "ADD:" + quoteName).WithButton(label: "Remove Upvote", customId: "REMOVE:" + quoteName).Build();
    }

}
