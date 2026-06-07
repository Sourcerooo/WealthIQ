using WealthIQ.Infrastructure.Ingest;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class FileSystemRawFileStoreTests
{
    [Fact]
    public void IngestDirectory_CopiesAllFilesIntoAnIsolatedSubfolder()
    {
        var src = Path.Combine(Path.GetTempPath(), "tp-src-" + Guid.NewGuid().ToString("N"));
        var audit = Path.Combine(Path.GetTempPath(), "tp-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Depot.csv"), "a");
        File.WriteAllText(Path.Combine(src, "Konto.csv"), "b");
        try
        {
            var store = new FileSystemRawFileStore(audit);
            var storedDir = store.IngestDirectory(src);

            Assert.True(Directory.Exists(storedDir));
            Assert.StartsWith(audit, storedDir);
            Assert.Equal(2, Directory.GetFiles(storedDir, "*.csv").Length);
        }
        finally
        {
            Directory.Delete(src, true);
            if (Directory.Exists(audit)) Directory.Delete(audit, true);
        }
    }
}
