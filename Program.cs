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

string seed = "https://example.com";
await frontier.SetupDbAsync(seed);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "crawler/0.1 (+https://github.com/joshuabissett/crawler)"
);

CrawlerWorker worker = new(1, httpClient, frontier);

using var sweepCancellation = new CancellationTokenSource();

Task sweepTask = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

    try
    {
        while (await timer.WaitForNextTickAsync(sweepCancellation.Token))
        {
            await frontier.SweepAsync(5);
        }
    }
    catch (OperationCanceledException)
        when (sweepCancellation.IsCancellationRequested)
    {
    }
});

try
{
    await worker.RunAsync();
}
finally
{
    sweepCancellation.Cancel();
    await sweepTask;
}
