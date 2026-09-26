using EVBlocker.Core.Policy;

namespace EVBlocker.Core.Tests;

public sealed class AllowListStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"evblocker-tests-{Guid.NewGuid():N}");

    private string StorePath => Path.Combine(_directory, "allowlist.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_NoFileYet_ReturnsEmptyList()
    {
        // First run. An empty list, not an error.
        AllowListDocument document = new AllowListStore(StorePath).Load();

        Assert.Empty(document.Apps);
        Assert.Equal(AllowListDocument.CurrentSchemaVersion, document.SchemaVersion);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var store = new AllowListStore(StorePath);
        var added = DateTimeOffset.Parse("2026-09-26T10:30:00+07:00", System.Globalization.CultureInfo.InvariantCulture);

        store.Save(new AllowListDocument
        {
            Apps =
            {
                new AllowedApp
                {
                    ExecutablePath = @"C:\Program Files\App\app.exe",
                    DisplayName = "An app",
                    Sha256 = "ABCD1234",
                    AddedAt = added,
                },
            },
        });

        AllowedApp loaded = Assert.Single(store.Load().Apps);

        Assert.Equal(@"C:\Program Files\App\app.exe", loaded.ExecutablePath);
        Assert.Equal("An app", loaded.DisplayName);
        Assert.Equal("ABCD1234", loaded.Sha256);
        Assert.Equal(added, loaded.AddedAt);
    }

    [Fact]
    public void Save_CreatesMissingDirectories()
    {
        string nested = Path.Combine(_directory, "a", "b", "allowlist.json");

        new AllowListStore(nested).Save(new AllowListDocument());

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var store = new AllowListStore(StorePath);

        store.Save(new AllowListDocument());
        store.Save(new AllowListDocument());

        Assert.Equal(new[] { "allowlist.json" }, Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_StampsTheCurrentSchemaVersion()
    {
        var store = new AllowListStore(StorePath);

        store.Save(new AllowListDocument { SchemaVersion = 0 });

        Assert.Equal(AllowListDocument.CurrentSchemaVersion, store.Load().SchemaVersion);
    }

    [Fact]
    public void Load_CorruptFile_Throws()
    {
        // Silently treating an unreadable allow-list as empty would revoke every decision the
        // user has made, and the firewall would be rewritten to match.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "{ this is not json");

        Assert.Throws<InvalidDataException>(() => new AllowListStore(StorePath).Load());
    }

    [Fact]
    public void Load_NewerSchema_Throws()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, """{"SchemaVersion": 99, "Apps": []}""");

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => new AllowListStore(StorePath).Load());

        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_JsonNull_Throws()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "null");

        Assert.Throws<InvalidDataException>(() => new AllowListStore(StorePath).Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankPath(string path)
    {
        Assert.Throws<ArgumentException>(() => new AllowListStore(path));
    }
}
