using Discord;
using Discord.Interactions;

public class QuoteNameAutoCompleter(SqliteService dbService) : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        var names = await dbService.GetRawQuoteCupritsAsync();
        var name =
            autocompleteInteraction.Data.Options.First(opt => opt.Name == "name").Value as string ?? "";
        
        if (string.IsNullOrWhiteSpace(name))
            return AutocompletionResult.FromSuccess(names
                .Select(q => new AutocompleteResult(q, q))
                .ToList());
            
        return AutocompletionResult.FromSuccess(names
            .Where(q => q.ToLower().Contains(name.ToLower()))
            .Select(q => new AutocompleteResult(q, q))
            .ToList());
    }
}