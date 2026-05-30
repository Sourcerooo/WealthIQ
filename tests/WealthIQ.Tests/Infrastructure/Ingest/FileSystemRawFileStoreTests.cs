using WealthIQ.Infrastructure.Ingest;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Ingest;

public sealed class FileSystemRawFileStoreTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-ingest-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Ingest_CopiesFileIntoRoot_ReturnsDestinationPathWithSameContent()
    {
        var sourceDir = Path.Combine(_temp, "src");
        var rootDir = Path.Combine(_temp, "audit");
        Directory.CreateDirectory(sourceDir);
        var source = Path.Combine(sourceDir, "statement.xml");
        File.WriteAllText(source, "<x/>");

        var store = new FileSystemRawFileStore(rootDir);
        var destination = store.Ingest(source);

        Assert.True(File.Exists(destination));
        Assert.StartsWith(rootDir, destination);
        Assert.Equal("statement.xml", Path.GetFileName(destination));
        Assert.Equal("<x/>", File.ReadAllText(destination));
    }

    [Fact]
    public void Ingest_MissingSource_Throws()
    {
        var store = new FileSystemRawFileStore(Path.Combine(_temp, "audit"));
        Assert.Throws<FileNotFoundException>(() => store.Ingest(Path.Combine(_temp, "nope.xml")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
