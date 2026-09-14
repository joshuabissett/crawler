using HtmlAgilityPack;

class CrawlerWorker
{
    private readonly int _id;
    private readonly HttpClient _httpClient;
    private readonly FrontierStore _frontier;
    private readonly RobotParse _robotParser;
    private readonly Dictionary<string, RobotRules?> _robotRules = new();

    public CrawlerWorker(int id, HttpClient httpClient, FrontierStore frontier)
    {
        _id = id;
        _httpClient = httpClient;
        _frontier = frontier;
        _robotParser = new RobotParse(httpClient);
    }

    public async Task RunAsync()
    {
        while (true)
        {
            string? url = await _frontier.ReadNextAsync();

            if (url == null)
            {
                await Task.Delay(250);
                continue;
            }

            await CrawlAsync(url);
        }
    }

    private async Task CrawlAsync(string url)
    {
        Uri uri = new(url);
        string host = uri.GetLeftPart(UriPartial.Authority);

        if (!_robotRules.TryGetValue(host, out RobotRules? rules))
        {
            rules = await _robotParser.ParseRobots(uri);
            _robotRules[host] = rules;
        }

        if (rules is not null && !rules.IsAllowed(uri))
        {
            Console.WriteLine($"Worker {_id}: blocked by robots.txt: {url}");
            await _frontier.MarkCompletedAsync(url);
            return;
        }

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync(url);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Worker {_id}: {ex.Message}");
            await _frontier.MarkFailedAsync(url);
            return;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                await _frontier.MarkFailedAsync(url);
                return;
            }

            string? contentType =
                response.Content.Headers.ContentType?.MediaType;

            if (contentType != "text/html" &&
                contentType != "application/xhtml+xml")
            {
                await _frontier.MarkCompletedAsync(url);
                return;
            }

            string html = await response.Content.ReadAsStringAsync();

            HtmlDocument doc = new();
            doc.LoadHtml(html);

            var links = doc.DocumentNode.SelectNodes("//a[@href]");

            if (links != null)
            {
                Uri baseUri = new(url);

                foreach (HtmlNode link in links)
                {
                    string href = link.GetAttributeValue("href", "");

                    if (!Uri.TryCreate(baseUri, href, out Uri? newUri))
                    {
                        continue;
                    }

                    if (newUri.Scheme != Uri.UriSchemeHttp &&
                        newUri.Scheme != Uri.UriSchemeHttps)
                    {
                        continue;
                    }

                    await _frontier.WriteNextAsync(newUri.ToString());
                }
            }

            await _frontier.MarkCompletedAsync(url);
        }
    }
}
