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