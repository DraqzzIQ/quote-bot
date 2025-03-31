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
        await using var cmd = new SqliteCommand("INSERT INTO Quote (Name, Content, Culprit, File, Upvotes, CreatedAt) VALUES (@Name, @Content, @Culprit, @File, @Upvotes, @CreatedAt)", _connection);
        cmd.Parameters.AddWithValue("@Name", quote.Name);
        cmd.Parameters.AddWithValue("@Content", quote.Content);
        cmd.Parameters.AddWithValue("@Culprit", quote.Culprit);
        cmd.Parameters.AddWithValue("@File", quote.FilePath);
        cmd.Parameters.AddWithValue("@Upvotes", quote.Upvotes);
        cmd.Parameters.AddWithValue("@CreatedAt", quote.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task EditQuoteAsync(string name, string newQuote)
    {
        await using var cmd = new SqliteCommand("UPDATE Quote SET Content = @Content WHERE Name = @Name", _connection);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@Content", newQuote);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RenameQuoteAsync(string name, string newName)
    {
        // Begin an immediate transaction to prevent other processes from making changes
        await using var transaction = _connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

        try
        {
            // Set foreign keys to deferred for this transaction
            await using var deferCmd = new SqliteCommand("PRAGMA defer_foreign_keys = ON;", _connection, transaction);
            await deferCmd.ExecuteNonQueryAsync();

            await using var updateQuoteCmd = new SqliteCommand(
                "UPDATE Quote SET Name = @NewName WHERE Name = @Name",
                _connection,
                transaction);
            updateQuoteCmd.Parameters.AddWithValue("@Name", name);
            updateQuoteCmd.Parameters.AddWithValue("@NewName", newName);
            await updateQuoteCmd.ExecuteNonQueryAsync();

            await using var updateUpvotesCmd = new SqliteCommand(
                "UPDATE UserUpvotes SET QuoteName = @NewName WHERE QuoteName = @Name",
                _connection,
                transaction);
            updateUpvotesCmd.Parameters.AddWithValue("@Name", name);
            updateUpvotesCmd.Parameters.AddWithValue("@NewName", newName);
            await updateUpvotesCmd.ExecuteNonQueryAsync();

            // Commit the transaction - constraints will be checked here
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            // Roll back on failure
            await transaction.RollbackAsync();
            throw ex;
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
        await using var cmd = new SqliteCommand("SELECT Name, Content, Culprit, File, Upvotes, CreatedAt FROM Quote WHERE Name = @Name", _connection);
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
                CreatedAt = DateTime.Parse(reader.GetString(5))
            };
        }
        return null;
    }

    public async Task<Quote?> GetRandomQuoteAsync()
    {
        await using var cmd = new SqliteCommand("SELECT Name, Content, Culprit, File, Upvotes, CreatedAt FROM Quote ORDER BY RANDOM() LIMIT 1", _connection);
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
                CreatedAt = DateTime.Parse(reader.GetString(5))
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
            string entry = $"{reader.GetString(0)}: {reader.GetString(1)} - {reader.GetString(2)}, {DateTime.Parse(reader.GetString(3)).ToString("dd.MM.yy")}";
            quoteNames.Add(entry);
        }

        return quoteNames;
    }

    public async Task<List<(string, string)>> GetUpvotedQuotesAsync(int count)
    {
        List<(string, string)> quotes = [];
        
        string query = "SELECT Upvotes, Name, Culprit, CreatedAt, Content FROM Quote ORDER BY Upvotes DESC LIMIT @Count";
        await using var cmd = new SqliteCommand(query, _connection);
        cmd.Parameters.AddWithValue("@Count", count);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string entry = $"{reader.GetString(0)}: {reader.GetString(1)} - {reader.GetString(2)}, {DateTime.Parse(reader.GetString(3)).ToString("dd.MM.yy")}";
            quotes.Add((entry, reader.GetString(4)));
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

    public async Task SetQuoteCreationDateAsync(string name, DateTime newCreatedAt)
    {
        await using var cmd = new SqliteCommand("UPDATE Quote SET CreatedAt = @CreatedAt WHERE Name = @Name", _connection);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@CreatedAt", newCreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }
    
    public async Task SetCulpritAsync(string name, string culprit)
    {
        await using var cmd = new SqliteCommand("UPDATE Quote SET Culprit = @Culprit WHERE Name = @Name", _connection);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@Culprit", culprit);

        await cmd.ExecuteNonQueryAsync();
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
