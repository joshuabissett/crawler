using Nager.PublicSuffix;
using Npgsql;

class FrontierStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly DomainParser _domainParser;

    public FrontierStore(NpgsqlDataSource dataSource, DomainParser domainParser)
    {
        _dataSource = dataSource;
        _domainParser = domainParser;
    }

    public async Task SetupDbAsync(string seed)
    {
        await using var setupCmd = _dataSource.CreateCommand(
            """
            CREATE TABLE IF NOT EXISTS frontier (
            id BIGSERIAL PRIMARY KEY,
            url TEXT NOT NULL UNIQUE,
            host TEXT NOT NULL,
            status SMALLINT NOT NULL DEFAULT 0,
            lease_claimed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS hosts (
            host TEXT PRIMARY KEY,
            next_allowed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """
        );

        await setupCmd.ExecuteNonQueryAsync();
        await WriteNextAsync(seed);

        await SweepAsync(0);
    }

    public async Task SweepAsync(int interval)
    {
        await using var sweepCmd = _dataSource.CreateCommand(
            """
                UPDATE frontier
                SET status = 0
                WHERE status = 1
                    AND lease_claimed_at + (@interval * INTERVAL '1 minutes') < NOW()
            """
    );
        sweepCmd.Parameters.AddWithValue("interval", interval);
        await sweepCmd.ExecuteNonQueryAsync();
    }

    public async Task WriteNextAsync(string url)
    {
        if(!_domainParser.TryParse(new Uri(url).Host, out DomainInfo? domainInfo))
        {
            return;
        }

        string? host = domainInfo.RegistrableDomain;
        if (host == null)
        {
            return;
        }

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
            SET status = 1,
                lease_claimed_at = NOW()
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