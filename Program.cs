using DotNetEnv;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;
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
var ruleProvider = new SimpleHttpRuleProvider();
await ruleProvider.BuildAsync();

var domainParser = new DomainParser(ruleProvider);

FrontierStore frontier = new(dataSource, domainParser);

string seed = "https://nlp.stanford.edu/IR-book/html/htmledition/crawler-architecture-1.html";
await frontier.SetupDbAsync(seed);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "crawler/0.1 (+https://github.com/joshuabissett/crawler)"
);

const int workerCount = 5;

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

var workers = Enumerable.Range(1, workerCount)
      .Select(id => new CrawlerWorker(id, httpClient, frontier).RunAsync())
      .ToArray();

try
{
    await Task.WhenAll(workers);
}
finally
{
    sweepCancellation.Cancel();
    await sweepTask;
}