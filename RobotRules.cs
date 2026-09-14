public class RobotRules
{
    public List<string> AllowedPaths { get; } = new();
    public List<string> DisallowedPaths { get; } = new();
    public int? CrawlDelay { get; set; }

    public bool IsAllowed(Uri uri)
    {
        string path = uri.PathAndQuery;

        int disallowLength = DisallowedPaths
            .Where(rule => path.StartsWith(rule, StringComparison.Ordinal))
            .Select(rule => rule.Length)
            .DefaultIfEmpty(-1)
            .Max();

        int allowLength = AllowedPaths
            .Where(rule => path.StartsWith(rule, StringComparison.Ordinal))
            .Select(rule => rule.Length)
            .DefaultIfEmpty(-1)
            .Max();

        return allowLength >= disallowLength;
    }
}
