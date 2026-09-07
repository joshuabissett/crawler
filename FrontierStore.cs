using Npgsql;

class FrontierStore
{
    private readonly NpgsqlDataSource _dataSource;

    public FrontierStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task SetupDbAsync(string seed)
    {
        await using var setupCmd = _dataSource.CreateCommand(
            """
            CREATE TABLE IF NOT EXISTS frontier (
            id BIGSERIAL PRIMARY KEY,
            url TEXT NOT NULL UNIQUE,
            host TEXT NOT NULL,
            status SMALLINT NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS hosts (
            host TEXT PRIMARY KEY,
            next_allowed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """
        );

        await setupCmd.ExecuteNonQueryAsync();
        await WriteNextAsync(seed);
    }

    public async Task WriteNextAsync(string url)
    {
        string host = new Uri(url).Host;

        await using var writeHostCmd = _dataSource.CreateCommand(
            """
            INSERT INTO hosts (host)
            VALUES (@host)
            ON CONFLICT (host) DO NOTHING
            """
        );

        writeHostCmd.Parameters.AddWithValue("host", host);
        await writeHostCmd.ExecuteNonQueryAsync();

        await using var writeFrontierCmd = _dataSource.CreateCommand(
            """
            INSERT INTO frontier (url, host)  
            VALUES (@url, @host)
            ON CONFLICT (url) DO NOTHING
            """
        );

        writeFrontierCmd.Parameters.AddWithValue("url", url);
        writeFrontierCmd.Parameters.AddWithValue("host", host);
        await writeFrontierCmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> ReadNextAsync()
    {
        await using var readCmd = _dataSource.CreateCommand(
            """
            WITH candidate AS (
                SELECT frontier.id, frontier.url, frontier.host
                FROM frontier
                JOIN hosts ON hosts.host = frontier.host
                WHERE frontier.status = 0
                    AND hosts.next_allowed_at <= NOW()
                ORDER BY frontier.id
                FOR UPDATE OF frontier, hosts SKIP LOCKED
                LIMIT 1
            ),
            update_host AS (
                UPDATE hosts
                SET next_allowed_at = NOW() + INTERVAL '30 seconds'
                FROM candidate
                WHERE hosts.host = candidate.host
            )
            UPDATE frontier
            SET status = 1
            FROM candidate
            WHERE frontier.id = candidate.id
            RETURNING frontier.url
            """
        );

        var result = await readCmd.ExecuteScalarAsync();

        return result as string;
    }

    public async Task MarkCompletedAsync(string url)
    {
        await using var completedCmd = _dataSource.CreateCommand(
            """
            UPDATE frontier
            SET status = 2
            WHERE url = $1
                AND status = 1
            """
        );

        completedCmd.Parameters.AddWithValue(url);

        await completedCmd.ExecuteNonQueryAsync();
    }

    public async Task MarkFailedAsync(string url)
    {
        await using var failedCmd = _dataSource.CreateCommand(
            """
            UPDATE frontier
            SET status = -1
            WHERE url = $1
                AND status = 1
        """
        );

        failedCmd.Parameters.AddWithValue(url);

        await failedCmd.ExecuteNonQueryAsync();
    }
}