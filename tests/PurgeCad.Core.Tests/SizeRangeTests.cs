using PurgeCad.Core.Scanning;
using Xunit;

namespace PurgeCad.Core.Tests;

public class SizeRangeTests
{
    const long MB = 1024 * 1024;

    [Theory]
    [InlineData(null, null, 50 * MB, true)]
    [InlineData(null, 10.0, 10 * MB, true)]
    [InlineData(null, 10.0, 10 * MB + 1, false)]
    [InlineData(2.0, null, 1 * MB, false)]
    [InlineData(2.0, 5.0, 3 * MB, true)]
    [InlineData(2.0, 5.0, 6 * MB, false)]
    public void Matches_sizes(double? min, double? max, long bytes, bool expected) =>
        Assert.Equal(expected, new SizeRange(min, max).Matches(bytes));

    [Theory]
    [InlineData("2,5", 2.5)]
    [InlineData(" 12 ", 12.0)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("-3", null)]
    public void Parses_megabytes(string text, double? expected) =>
        Assert.Equal(expected, SizeRange.ParseMb(text));

    [Fact]
    public void Name_and_size_filters_combine()
    {
        var scan = new ScanResult { Root = "/x" };
        scan.Drawings.Add(new FoundDrawing("/x/propunere mare.dwg", "propunere mare.dwg", DrawingKind.Dwg, 30 * MB, false));
        scan.Drawings.Add(new FoundDrawing("/x/propunere mica.dwg", "propunere mica.dwg", DrawingKind.Dwg, 1 * MB, false));
        scan.Drawings.Add(new FoundDrawing("/x/plan.dwg", "plan.dwg", DrawingKind.Dwg, 1 * MB, false));
        scan.Auxiliary.Add(new FoundAuxiliary("/x/propunere mare.bak", "propunere mare.bak", AuxiliaryKind.Backup, 1));

        var result = ScanFilter.Apply(scan, new NameFilter("propunere"), new SizeRange(null, 10));

        Assert.Equal("propunere mica.dwg", Assert.Single(result.Drawings).RelativePath);
        Assert.Empty(result.Auxiliary); // the .bak of a drawing that was filtered out goes too
    }
}
