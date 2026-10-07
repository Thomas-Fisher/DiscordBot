namespace DiscordBot.Api;

public static class DotEnvFile
{
    private const string FileName = ".env";
    private const string SolutionFileName = "DiscordBot.slnx";

    public static IReadOnlyList<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        var pairs = new List<(string, string)>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=');

            if (line.Length == 0 || line.StartsWith('#') || separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
            {
                value = value[1..^1];
            }

            pairs.Add((key, value));
        }

        return pairs;
    }

    public static string? Find(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return null;
            }
        }

        return null;
    }

    public static int LoadForDevelopment()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var path = Find(Directory.GetCurrentDirectory()) ?? Find(AppContext.BaseDirectory);
        return path == null ? 0 : Load(path);
    }

    public static int Load(string path)
    {
        var set = 0;

        foreach (var (key, value) in Parse(File.ReadAllLines(path)))
        {
            if (Environment.GetEnvironmentVariable(key) == null)
            {
                Environment.SetEnvironmentVariable(key, value);
                set++;
            }
        }

        return set;
    }
}
