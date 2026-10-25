using PurgeCad.Core.Branding;

namespace PurgeCad.Core.Tests;

public class BrandTests
{
    [Theory]
    [InlineData("en", "https://syntelab.com/apps/purge-cad")]
    [InlineData("ro", "https://syntelab.com/apps/ro/purge-cad")]
    [InlineData("pt-BR", "https://syntelab.com/apps/pt-BR/purge-cad")]
    public void AppPageUrlFollowsLanguage(string code, string expected) => Assert.Equal(expected, Brand.AppPageUrl(code));

    [Fact]
    public void AttributionNoticeMentionsSyntelabAndLinks()
    {
        Assert.Contains("Syntelab", Brand.AttributionNotice);
        Assert.Contains("https://syntelab.com/apps/purge-cad", Brand.AttributionNotice);
        Assert.Contains("https://syntelab.com/apps", Brand.AttributionNotice);
    }
}
