public class RobotParse
{
    private readonly HttpClient _httpClient;

    public RobotParse(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
    public async Task<RobotRules?> ParseRobots(Uri uri)
    {
        string crawlerName =
        _httpClient.DefaultRequestHeaders.UserAgent
            .FirstOrDefault()
            ?.Product?
            .Name ?? "*";


        Uri robotsUri = new(
            uri.GetLeftPart(UriPartial.Authority) + "/robots.txt"
        );

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync(robotsUri);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return null;

            string robotsTxt = await response.Content.ReadAsStringAsync();

            RobotRules rules = new();

            bool appliesToUs = false;
            bool readingAgents = true;

            foreach (string rawLine in robotsTxt.Split('\n'))
            {
                string line = rawLine.Split('#', 2)[0].Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    appliesToUs = false;
                    readingAgents = true;
                    continue;
                }

                if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!readingAgents)
                        appliesToUs = false;

                    string agent = line["User-agent:".Length..].Trim();
                    appliesToUs |=
                        agent == "*" ||
                        crawlerName.Contains(agent, StringComparison.OrdinalIgnoreCase);
                    readingAgents = true;

                    continue;
                }

                readingAgents = false;

                if (!appliesToUs)
                    continue;

                if (line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line["Disallow:".Length..].Trim();

                    if (!string.IsNullOrEmpty(path))
                        rules.DisallowedPaths.Add(path);
                }

                else if (line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line["Allow:".Length..].Trim();

                    if (!string.IsNullOrEmpty(path))
                        rules.AllowedPaths.Add(path);
                }

                else if (line.StartsWith("Crawl-delay:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line["Crawl-delay:".Length..].Trim();

                    if (int.TryParse(value, out int delay))
                        rules.CrawlDelay = delay;
                }
            }

            return rules;
        }
    }
}
