using PurgeCad.Core.Scanning;
using Xunit;

namespace PurgeCad.Core.Tests;

public class NameFilterTests
{
    [Theory]
    [InlineData("", @"C:\lucrari\plan.dwg", true)]
    [InlineData("propunere", @"C:\lucrari\PAD Propunere 2.dwg", true)]
    [InlineData("propunere", @"C:\propunere\plan.dwg", false)]          // folder names do not count
    [InlineData("scoala", @"C:\x\Propunere_ȘCOALĂ.dwg", true)]          // diacritics and case ignored
    [InlineData("propunere, avizare", @"C:\x\plan avizare.dwg", true)]
    [InlineData("PAD*propunere", @"C:\x\PAD NC 54186 propunere.dwg", true)]
    [InlineData("PAD*propunere", @"C:\x\propunere PAD.dwg", false)]
    [InlineData("propunere, -vechi", @"C:\x\propunere vechi.dwg", false)]
    [InlineData("-vechi", @"C:\x\plan.dwg", true)]
    [InlineData("dwg", @"C:\x\plan.dwg", false)]                         // the extension is not part of the name
    public void Matches_names(string filter, string path, bool expected) =>
        Assert.Equal(expected, new NameFilter(filter).Matches(path));

    [Fact]
    public void Apply_keeps_auxiliary_files_of_matching_drawings_only()
    {
        var scan = new ScanResult { Root = @"C:\x" };
        scan.Drawings.Add(new FoundDrawing(@"C:\x\propunere.dwg", "propunere.dwg", DrawingKind.Dwg, 1, false));
        scan.Drawings.Add(new FoundDrawing(@"C:\x\plan.dwg", "plan.dwg", DrawingKind.Dwg, 1, false));
        scan.Auxiliary.Add(new FoundAuxiliary(@"C:\x\propunere.bak", "propunere.bak", AuxiliaryKind.Backup, 1));
        scan.Auxiliary.Add(new FoundAuxiliary(@"C:\x\plan.bak", "plan.bak", AuxiliaryKind.Backup, 1));

        var result = new NameFilter("propunere").Apply(scan);

        Assert.Equal("propunere.dwg", Assert.Single(result.Drawings).RelativePath);
        Assert.Equal("propunere.bak", Assert.Single(result.Auxiliary).RelativePath);
    }
}
