using EVBlocker.Core.Ssh;

namespace EVBlocker.Core.Tests;

public sealed class SshSetupTaskTests
{
    [Fact]
    public void TaskXml_RunsTheExecutableWithTheSetupSwitch()
    {
        string xml = SshSetupTask.BuildTaskXml(@"C:\Program Files\EVBlocker\EVBlocker.exe");

        Assert.Contains("<Command>C:\\Program Files\\EVBlocker\\EVBlocker.exe</Command>", xml);
        Assert.Contains($"<Arguments>{SshSetupTask.SetupSwitch}</Arguments>", xml);
    }

    [Fact]
    public void TaskXml_RunsAsLocalSystemAtBoot()
    {
        string xml = SshSetupTask.BuildTaskXml(@"C:\EVBlocker.exe");

        // S-1-5-18 is LOCAL SYSTEM; the name is localised, the SID is not.
        Assert.Contains("<UserId>S-1-5-18</UserId>", xml);
        Assert.Contains("<BootTrigger>", xml);
    }

    [Fact]
    public void TaskXml_EscapesAPathThatContainsMarkup()
    {
        string xml = SshSetupTask.BuildTaskXml(@"C:\a & b\EVBlocker.exe");

        Assert.Contains("C:\\a &amp; b\\EVBlocker.exe", xml);
    }
}
