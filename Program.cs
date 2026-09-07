using DotNetEnv;
using Npgsql;

Env.Load();

var connectionString = Environment.GetEnvironmentVariable(
    "DATABASE_CONNECTION_STRING"
);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "DATABASE_CONNECTION_STRING is not set."
    );
}

NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);

FrontierStore frontier = new(dataSource);

string seed = "https://example.com/";
await frontier.SetupDbAsync(seed);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "JoshuaBissettCrawler/0.1 (+https://github.com/joshuabissett/crawler)"
);

CrawlerWorker worker = new(1, httpClient, frontier);
await worker.RunAsync();