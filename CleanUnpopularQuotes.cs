using Quartz;

public class CleanUnpopularQuotes(SqliteService dbService) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await dbService.DeleteQuotesWithZeroUpvotesAsync();
    }
}
