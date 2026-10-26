# crawler

A web crawler I wrote from scratch in C#. It uses Postgres to keep track of which URLs to visit, runs multiple workers at once, and tries to be polite to the sites it crawls.

## How it works

1. It starts with one seed URL and saves it in a `frontier` table in Postgres.
2. Five workers each grab the next URL from that table.
3. Each worker checks the site's `robots.txt` and skips the page if it isn't allowed.
4. It downloads the page, pulls out all the links, and adds any new ones to the frontier.
5. It marks the URL as done (or failed) and grabs the next one.

Every URL has a status:

| Status | Meaning |
| --- | --- |
| `0` | Waiting |
| `1` | In progress |
| `2` | Done |
| `-1` | Failed |

## Some things I had to figure out

**Workers grabbing the same URL.** With five workers hitting the database at the same time, two of them could grab the same row. I used `FOR UPDATE SKIP LOCKED` so each worker skips rows someone else already has.

**Not spamming sites.** There's a `hosts` table that tracks when each site can be hit next. When a URL is claimed, that site gets a 30 second cooldown. Sites are grouped by their actual domain using the public suffix list, so `a.example.com` and `b.example.com` count as the same site.

**Workers crashing.** If a worker dies in the middle of a page, that URL would be stuck "in progress" forever. A sweeper runs every minute and puts anything stuck for over five minutes back in the queue.

**robots.txt.** I wrote a small parser for it. It reads `Allow` and `Disallow` rules, and when they conflict the longest matching rule wins.

**Duplicate URLs.** I strip the `#fragment` off URLs, and the `url` column is unique, so the same page never gets added twice.

## Running it

You need the .NET 10 SDK and a Postgres database.

Make a `.env` file in the project folder:

```dotenv
DATABASE_CONNECTION_STRING=Host=localhost;Database=crawler;Username=postgres;Password=yourpassword
```

Then run:

```bash
dotnet run
```

The tables get created automatically. To start from a different page, change `seed` in `Program.cs`.

To check progress:

```sql
SELECT status, COUNT(*) FROM frontier GROUP BY status;
```

## Files

- `Program.cs` sets everything up and starts the workers and the sweeper.
- `FrontierStore.cs` handles all the database stuff.
- `CrawlerWorker.cs` is the actual crawl loop.
- `RobotParse.cs` and `RobotRules.cs` handle `robots.txt`.
