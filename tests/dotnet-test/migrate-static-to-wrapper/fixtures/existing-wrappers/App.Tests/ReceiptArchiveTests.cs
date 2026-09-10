using App.Services;
using Xunit;

namespace App.Tests;

public sealed class ReceiptArchiveTests
{
    [Fact]
    public void Save_then_load_round_trips_text()
    {
        var path = Path.GetTempFileName();
        try
        {
            var archive = new ReceiptArchive();
            archive.Save(path, "receipt-42");

            Assert.Equal("receipt-42", archive.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
