using DiscordBot.Api;

namespace DiscordBot.Tests.Unit.Api;

public class DotEnvFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotenv-" + Guid.NewGuid().ToString("N"));

    public DotEnvFileTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Parse_CommentsBlankLinesAndQuotes_ReadsOnlyTheSettings()
    {
        var pairs = DotEnvFile.Parse(
        [
            "# a comment",
            "",
            "  Discord__BotToken = abc  ",
            "Jwt__Secret=\"with spaces\"",
            "Quoted='single'",
            "Url=http://x/?a=b=c",
            "no-equals-sign",
            "=novalue-key"
        ]);

        Assert.Equal(
            [("Discord__BotToken", "abc"), ("Jwt__Secret", "with spaces"), ("Quoted", "single"), ("Url", "http://x/?a=b=c")],
            pairs);
    }

    [Fact]
    public void Find_EnvFileInAParentDirectory_ReturnsTheNearestOne()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "a", "b")).FullName;
        File.WriteAllText(Path.Combine(_root, ".env"), "X=1");
        File.WriteAllText(Path.Combine(_root, "a", ".env"), "X=2");

        Assert.Equal(Path.Combine(_root, "a", ".env"), DotEnvFile.Find(nested));
    }

    [Fact]
    public void Find_StopsAtTheRepositoryRoot_SoAStrayEnvFurtherUpIsNeverRead()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        var project = Directory.CreateDirectory(Path.Combine(repo, "Project")).FullName;
        File.WriteAllText(Path.Combine(_root, ".env"), "X=stray");
        File.WriteAllText(Path.Combine(repo, "DiscordBot.slnx"), "");

        Assert.Null(DotEnvFile.Find(project));
    }

    [Fact]
    public void Load_SetsMissingVariablesAndLeavesTheOnesAlreadySet()
    {
        var fresh = "DOTENV_TEST_FRESH_" + Guid.NewGuid().ToString("N");
        var existing = "DOTENV_TEST_EXISTING_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(existing, "from-the-environment");
        var path = Path.Combine(_root, ".env");
        File.WriteAllLines(path, [$"{fresh}=from-the-file", $"{existing}=from-the-file"]);

        try
        {
            var set = DotEnvFile.Load(path);

            Assert.Equal(1, set);
            Assert.Equal("from-the-file", Environment.GetEnvironmentVariable(fresh));
            Assert.Equal("from-the-environment", Environment.GetEnvironmentVariable(existing));
        }
        finally
        {
            Environment.SetEnvironmentVariable(fresh, null);
            Environment.SetEnvironmentVariable(existing, null);
        }
    }
}
