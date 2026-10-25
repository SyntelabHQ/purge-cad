using PurgeCad.Core.Updates;
using Xunit;

namespace PurgeCad.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("PurgeCAD-0.3.1", "0.3.1")]
    [InlineData("v2", "2.0.0")]
    public void Versions_are_read_from_tags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Fact]
    public void Newer_release_is_offered()
    {
        const string json = """{"tag_name":"v0.2.0","html_url":"https://github.com/SyntelabHQ/purge-cad/releases/tag/v0.2.0","body":"notes","draft":false,"prerelease":false}""";
        var update = UpdateChecker.Parse(json, new Version(0, 1, 0));
        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 0), update.Version);
        Assert.EndsWith("/v0.2.0", update.PageUrl);
    }

    [Theory]
    [InlineData("""{"tag_name":"v0.1.0","draft":false,"prerelease":false}""")]   // same version
    [InlineData("""{"tag_name":"v0.0.9","draft":false,"prerelease":false}""")]   // older
    [InlineData("""{"tag_name":"v0.9.0","draft":false,"prerelease":true}""")]    // pre-release
    public void Same_older_or_prerelease_is_not_offered(string json) =>
        Assert.Null(UpdateChecker.Parse(json, new Version(0, 1, 0)));
}
