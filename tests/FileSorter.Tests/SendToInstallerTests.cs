using FileSorter.Core;
using Xunit;

namespace FileSorter.Tests;

public class StartupArgsTests
{
    [Fact]
    public void Parse_NoArgs_Returns_Normal()
    {
        var a = StartupArgs.Parse(Array.Empty<string>());
        Assert.Equal(StartupMode.Normal, a.Mode);
        Assert.Empty(a.Files);
    }

    [Fact]
    public void Parse_SendTo_OneFile()
    {
        var a = StartupArgs.Parse(new[] { "--sendto", @"C:\in\test.jpg" });
        Assert.Equal(StartupMode.SendTo, a.Mode);
        Assert.Equal(new[] { @"C:\in\test.jpg" }, a.Files);
    }

    [Fact]
    public void Parse_Install_Returns_Install()
    {
        var a = StartupArgs.Parse(new[] { "--install" });
        Assert.Equal(StartupMode.Install, a.Mode);
    }

    [Fact]
    public void Parse_Uninstall_Returns_Uninstall()
    {
        var a = StartupArgs.Parse(new[] { "--uninstall" });
        Assert.Equal(StartupMode.Uninstall, a.Mode);
    }

    [Fact]
    public void Parse_InstallSendTo_Returns_InstallSendTo()
    {
        var a = StartupArgs.Parse(new[] { "--install-sendto" });
        Assert.Equal(StartupMode.InstallSendTo, a.Mode);
    }
}

public class SendToInstallerTests
{
    [Fact]
    public void IsInstalled_True_When_Lnk_Exists()
    {
        var sendToDir = Path.Combine(Path.GetTempPath(), "sendto_test_" + Guid.NewGuid());
        Directory.CreateDirectory(sendToDir);
        var exe = Path.Combine(sendToDir, "filesorter.exe");
        File.WriteAllBytes(exe, new byte[] { 0 });
        // Create a fake .lnk (file with .lnk extension is enough for IsInstalled)
        File.WriteAllText(Path.Combine(sendToDir, "FileSorter.lnk"), "");

        Assert.True(SendToInstaller.IsInstalled(exe, sendToDir));

        Directory.Delete(sendToDir, recursive: true);
    }
}