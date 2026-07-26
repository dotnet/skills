using System.IO.Compression;
using System.Text;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Submissions;

public sealed class NormalizedPackageTests
{
    [Fact]
    public void Output_has_stable_order_timestamps_and_content()
    {
        var files = new Dictionary<string, byte[]> { ["z.txt"] = Encoding.UTF8.GetBytes("z"), ["a.txt"] = Encoding.UTF8.GetBytes("a") };
        var first = SkillPackageParser.Normalize(files);
        var second = SkillPackageParser.Normalize(files);
        Assert.Equal(first, second);
        using var zip = new ZipArchive(new MemoryStream(first));
        Assert.Equal(["a.txt", "z.txt"], zip.Entries.Select(x => x.FullName));
        Assert.All(zip.Entries, x => Assert.Equal(1980, x.LastWriteTime.Year));
        Assert.Equal(files.Sum(x => x.Value.Length), zip.Entries.Sum(x => x.Length));
    }
}
