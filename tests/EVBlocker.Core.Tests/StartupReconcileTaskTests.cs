using System.Xml.Linq;
using EVBlocker.Core.Startup;

namespace EVBlocker.Core.Tests;

/// <summary>Records what it was asked to register, so the definition can be inspected.</summary>
internal sealed class FakeScheduledTaskHost : IScheduledTaskHost
{
    private readonly Dictionary<string, string> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = new();

    public string? XmlFor(string taskName) => _tasks.GetValueOrDefault(taskName);

    public bool Exists(string taskName) => _tasks.ContainsKey(taskName);

    public void Register(string taskName, string taskXml)
    {
        Calls.Add($"Register:{taskName}");
        _tasks[taskName] = taskXml;
    }

    public void Remove(string taskName)
    {
        Calls.Add($"Remove:{taskName}");
        _tasks.Remove(taskName);
    }
}

public sealed class StartupReconcileTaskTests : IDisposable
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly FakeScheduledTaskHost _host = new();
    private readonly string _executable = Path.Combine(Path.GetTempPath(), $"evb-{Guid.NewGuid():N}.exe");

    public StartupReconcileTaskTests() => File.WriteAllText(_executable, "not a real executable");

    public void Dispose() => File.Delete(_executable);

    private StartupReconcileTask Sut => new(_host);

    private XElement RegisteredXml()
    {
        Sut.Install(_executable);
        return XDocument.Parse(_host.XmlFor(StartupReconcileTask.TaskName)!).Root!;
    }

    private static string? Value(XElement root, string name) =>
        root.Descendants(Ns + name).FirstOrDefault()?.Value;

    [Fact]
    public void Install_ThenIsInstalled()
    {
        StartupReconcileTask sut = Sut;

        Assert.False(sut.IsInstalled());
        sut.Install(_executable);
        Assert.True(sut.IsInstalled());
    }

    [Fact]
    public void Uninstall_RemovesIt()
    {
        StartupReconcileTask sut = Sut;
        sut.Install(_executable);

        sut.Uninstall();

        Assert.False(sut.IsInstalled());
    }

    [Fact]
    public void Install_MissingExecutable_IsRefused()
    {
        // A task pointing at a missing file fails silently every boot, and the drift it was meant
        // to catch goes on being uncorrected.
        Assert.Throws<FileNotFoundException>(
            () => Sut.Install(Path.Combine(Path.GetTempPath(), "definitely-not-here.exe")));
    }

    [Fact]
    public void TriggersOnBoot()
    {
        Assert.NotEmpty(RegisteredXml().Descendants(Ns + "BootTrigger"));
    }

    [Fact]
    public void WaitsBeforeRunning()
    {
        // Running the instant the machine starts would race MpsSvc finishing its own work, and a
        // reconcile that reads a half-applied configuration would rewrite rules for no reason.
        Assert.Equal("PT30S", Value(RegisteredXml(), "Delay"));
    }

    [Fact]
    public void RunsAsLocalSystem()
    {
        // By SID, not by name: "SYSTEM" is localised and would not resolve on every machine.
        Assert.Equal("S-1-5-18", Value(RegisteredXml(), "UserId"));
        Assert.Equal("HighestAvailable", Value(RegisteredXml(), "RunLevel"));
    }

    [Fact]
    public void RunsTheApplicationInReconcileMode()
    {
        XElement root = RegisteredXml();

        Assert.Equal(_executable, Value(root, "Command"));
        Assert.Equal(StartupReconcileTask.ReconcileSwitch, Value(root, "Arguments"));
    }

    [Fact]
    public void DoesNotWaitForANetwork()
    {
        // The policy being repaired may be the reason the machine appears to have no network.
        Assert.Equal("false", Value(RegisteredXml(), "RunOnlyIfNetworkAvailable"));
    }

    [Fact]
    public void BatterySettingsDoNotBlockIt()
    {
        XElement root = RegisteredXml();

        Assert.Equal("false", Value(root, "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value(root, "StopIfGoingOnBatteries"));
    }

    [Fact]
    public void PathWithXmlSpecialCharacters_StaysWellFormed()
    {
        string awkward = Path.Combine(Path.GetTempPath(), $"R&D (v2) {Guid.NewGuid():N}.exe");
        File.WriteAllText(awkward, "x");

        try
        {
            Sut.Install(awkward);
            XElement root = XDocument.Parse(_host.XmlFor(StartupReconcileTask.TaskName)!).Root!;

            Assert.Equal(awkward, Value(root, "Command"));
        }
        finally
        {
            File.Delete(awkward);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Install_RejectsBlankPath(string path)
    {
        Assert.Throws<ArgumentException>(() => Sut.Install(path));
    }
}
