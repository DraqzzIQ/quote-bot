using Discord;
using Discord.Interactions;

public class QuoteCulpritAutoCompleter(SqliteService dbService) : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        var culprits = (await dbService.GetRawQuoteCupritsAsync()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var culprit =
            autocompleteInteraction.Data.Options.First(opt => opt.Name == "culprit").Value as string ?? "";
        
        if (string.IsNullOrWhiteSpace(culprit))
            return AutocompletionResult.FromSuccess(culprits
            .Select(q => new AutocompleteResult(q, q))
            .ToList().Take(25));
            
        return AutocompletionResult.FromSuccess(culprits
            .Where(q => q.ToLower().Contains(culprit.ToLower()))
            .Select(q => new AutocompleteResult(q, q))
            .ToList().Take(25));
    }
}