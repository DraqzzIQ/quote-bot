using Microsoft.Data.Sqlite;

public class SqliteService
{
    private readonly SqliteConnection _connection = CreateSqliteConnection();

    public SqliteService()
    {
        using var cmd = new SqliteCommand("CREATE TABLE IF NOT EXISTS Quote (Name TEXT PRIMARY KEY, Content TEXT, Culprit TEXT, File TEXT, Upvotes INTEGER, CreatedAt TEXT)", _connection);
        cmd.ExecuteNonQuery();

        using var cmd2 = new SqliteCommand("CREATE TABLE IF NOT EXISTS UserUpvotes (UserId TEXT, QuoteName TEXT, PRIMARY KEY (UserId, QuoteName), FOREIGN KEY (QuoteName) REFERENCES Quote(Name))", _connection);
        cmd2.ExecuteNonQuery();
    }

    public async Task AddQuoteAsync(Quote quote)
    {
        await using var cmd = new SqliteCommand("INSERT INTO Quote (Name, Content, Culprit, File, Upvotes, CreatedAt, RecordedAt) VALUES (@Name, @Content, @Culprit, @File, @Upvotes, @CreatedAt, @RecordedAt)", _connection);
        cmd.Parameters.AddWithValue("@Name", quote.Name);
        cmd.Parameters.AddWithValue("@Content", quote.Content);
        cmd.Parameters.AddWithValue("@Culprit", quote.Culprit);
        cmd.Parameters.AddWithValue("@File", quote.FilePath);
        cmd.Parameters.AddWithValue("@Upvotes", quote.Upvotes);
        cmd.Parameters.AddWithValue("@CreatedAt", quote.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@RecordedAt", quote.RecordedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task EditQuoteAsync(string name, string? newName, string? newQuote, string? newCulprit, DateTime? newCreatedAt)
    {
        string startingQuery = "UPDATE Quote SET";
        await using var cmd = new SqliteCommand(startingQuery, _connection);


        if (!String.IsNullOrEmpty(newQuote))
        {
            cmd.CommandText += " Content = @Content,";
            cmd.Parameters.AddWithValue("@Content", newQuote);
        }

        if (!String.IsNullOrEmpty(newCulprit))
        {
            cmd.CommandText += " Culprit = @Culprit,";
            cmd.Parameters.AddWithValue("@Culprit", newCulprit);
        }

        if (newCreatedAt != null)
        {
            cmd.CommandText += " CreatedAt = @CreatedAt,";
            cmd.Parameters.AddWithValue("@CreatedAt", newCreatedAt.Value.ToString("o"));
        }

        if (!String.IsNullOrEmpty(newName))
        {
            await using var transaction = _connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            cmd.Transaction = transaction;
            try
            {
                await using var deferCmd = new SqliteCommand("PRAGMA defer_foreign_keys = ON;", _connection, transaction);
                await deferCmd.ExecuteNonQueryAsync();

                cmd.CommandText += " Name = @NewName WHERE Name = @OldName";
                cmd.Parameters.AddWithValue("@NewName", newName);
                cmd.Parameters.AddWithValue("@OldName", name);
                await cmd.ExecuteNonQueryAsync();

                await using var updateUpvotesCmd = new SqliteCommand(
                    "UPDATE UserUpvotes SET QuoteName = @NewName WHERE QuoteName = @OldName",
                    _connection,
                    transaction);

                updateUpvotesCmd.Parameters.AddWithValue("@OldName", name);
                updateUpvotesCmd.Parameters.AddWithValue("@NewName", newName);
                await updateUpvotesCmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw ex;
            }
        }
        else
        {
            if (cmd.CommandText == startingQuery)
            {
                return;
            }

            cmd.CommandText = cmd.CommandText.Remove(cmd.CommandText.Length - 1);
            cmd.CommandText += " WHERE Name = @Name";
            cmd.Parameters.AddWithValue("@Name", name);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DeleteQuoteAsync(string name)
    {
        await using var deleteUpvotesCmd = new SqliteCommand("DELETE FROM UserUpvotes WHERE QuoteName = @Name", _connection);
        deleteUpvotesCmd.Parameters.AddWithValue("@Name", name);
        await deleteUpvotesCmd.ExecuteNonQueryAsync();

        await using var deleteQuoteCmd = new SqliteCommand("DELETE FROM Quote WHERE Name = @Name", _connection);
        deleteQuoteCmd.Parameters.AddWithValue("@Name", name);
        await deleteQuoteCmd.ExecuteNonQueryAsync();
    }

    public async Task AttachMediaAsync(string name, string fileName)
    {
        await using var cmd = new SqliteCommand("UPDATE Quote SET File = @File WHERE Name = @Name", _connection);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@File", fileName);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveMediaAsync(string name)
    {
        await using var cmd = new SqliteCommand("UPDATE Quote SET File = '' WHERE Name = @Name", _connection);
        cmd.Parameters.AddWithValue("@Name", name);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Quote?> GetQuoteAsync(string name)
    {
        await using var cmd = new SqliteCommand("SELECT Name, Content, Culprit, File, Upvotes, CreatedAt, RecordedAt FROM Quote WHERE Name = @Name", _connection);
        cmd.Parameters.AddWithValue("@Name", name);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Quote
            {
                Name = reader.GetString(0),
                Content = reader.GetString(1),
                Culprit = reader.GetString(2),
                FilePath = reader.GetString(3),
                Upvotes = reader.GetInt32(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                RecordedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        return null;
    }

    public async Task<Quote?> GetRandomQuoteAsync(int minUpvotes = 0)
    {
        await using var cmd = new SqliteCommand("SELECT Name, Content, Culprit, File, Upvotes, CreatedAt, RecordedAt FROM Quote WHERE Upvotes >= @MinUpvotes ORDER BY RANDOM() LIMIT 1", _connection);
        cmd.Parameters.AddWithValue("@MinUpvotes", minUpvotes);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Quote
            {
                Name = reader.GetString(0),
                Content = reader.GetString(1),
                Culprit = reader.GetString(2),
                FilePath = reader.GetString(3),
                Upvotes = reader.GetInt32(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                RecordedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        return null;
    }

    public async Task<List<string>> GetAllQuotesAsync(SortType sortType, string? author = null, int maxCount = -1)
    {
        var quoteNames = new List<string>();
        string query = "SELECT Upvotes, Name, Culprit, CreatedAt FROM Quote";
        if (author != null)
        {
            query += " WHERE Culprit = @Culprit";
        }
        query += " ORDER BY " + (sortType == SortType.Upvotes ? "Upvotes DESC" : "CreatedAt");
        if (maxCount != -1)
            query += $" LIMIT {maxCount}";

        await using var cmd = new SqliteCommand(query, _connection);
        if (author != null)
        {
            cmd.Parameters.AddWithValue("@Culprit", author);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string entry = $"{reader.GetString(0)}⬆️: {reader.GetString(1)} - {reader.GetString(2)}, {DateTime.Parse(reader.GetString(3)).ToString("dd.MM.yy")}";
            quoteNames.Add(entry);
        }

        return quoteNames;
    }

    public async Task<List<Quote>> GetUpvotedQuotesAsync(int count)
    {
        List<Quote> quotes = [];

        string query = "SELECT Name, Content, Culprit, File, Upvotes, CreatedAt, RecordedAt FROM Quote ORDER BY Upvotes DESC LIMIT @Count";
        await using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@Count", count);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            quotes.Add(new Quote
            {
                Name = reader.GetString(0),
                Content = reader.GetString(1),
                Culprit = reader.GetString(2),
                FilePath = reader.GetString(3),
                Upvotes = reader.GetInt32(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                RecordedAt = DateTime.Parse(reader.GetString(6))
            });
        }

        return quotes;
    }

    public async Task UpvoteQuoteAsync(string userId, string quoteName)
    {
        await using var insertCmd = new SqliteCommand("INSERT INTO UserUpvotes (UserId, QuoteName) VALUES (@UserId, @QuoteName)", _connection);
        insertCmd.Parameters.AddWithValue("@UserId", userId);
        insertCmd.Parameters.AddWithValue("@QuoteName", quoteName);
        await insertCmd.ExecuteNonQueryAsync();

        await using var updateCmd = new SqliteCommand("UPDATE Quote SET UpVotes = UpVotes + 1 WHERE Name = @QuoteName", _connection);
        updateCmd.Parameters.AddWithValue("@QuoteName", quoteName);
        await updateCmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveUpvoteAsync(string userId, string quoteName)
    {
        await using var deleteCmd = new SqliteCommand("DELETE FROM UserUpvotes WHERE UserId = @UserId AND QuoteName = @QuoteName", _connection);
        deleteCmd.Parameters.AddWithValue("@UserId", userId);
        deleteCmd.Parameters.AddWithValue("@QuoteName", quoteName);
        int rowsAffected = await deleteCmd.ExecuteNonQueryAsync();

        if (rowsAffected > 0)
        {
            await using var updateCmd = new SqliteCommand("UPDATE Quote SET UpVotes = MAX(0, UpVotes - 1) WHERE Name = @QuoteName", _connection);
            updateCmd.Parameters.AddWithValue("@QuoteName", quoteName);
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<bool> HasUserUpvotedAsync(string userId, string quoteName)
    {
        await using var checkCmd = new SqliteCommand("SELECT COUNT(*) FROM UserUpvotes WHERE UserId = @UserId AND QuoteName = @QuoteName", _connection);
        checkCmd.Parameters.AddWithValue("@UserId", userId);
        checkCmd.Parameters.AddWithValue("@QuoteName", quoteName);

        var count = (long)await checkCmd.ExecuteScalarAsync();
        return count > 0;
    }

    public async Task<List<string>> GetRawQuoteNamesAsync()
    {
        var quoteNames = new List<string>();
        string query = "SELECT Name FROM Quote";

        await using var cmd = new SqliteCommand(query, _connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            quoteNames.Add(reader.GetString(0));
        }

        return quoteNames;
    }

    public async Task<List<string>> GetRawQuoteCupritsAsync()
    {
        var culprits = new List<string>();
        string query = "SELECT Culprit FROM Quote";

        await using var cmd = new SqliteCommand(query, _connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            culprits.Add(reader.GetString(0));
        }

        return culprits;
    }

    public async Task DeleteQuotesWithZeroUpvotesAsync()
    {
        await using var deleteCmd = new SqliteCommand("DELETE FROM Quote WHERE Upvotes = 0 AND RecordedAt < @TwoWeeksAgo", _connection);
        deleteCmd.Parameters.AddWithValue("@TwoWeeksAgo", DateTime.UtcNow.AddDays(-14));
        await deleteCmd.ExecuteNonQueryAsync();
    }

    private static SqliteConnection CreateSqliteConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException("DB_CONNECTION", "Database connection string is not set.");
        }

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
