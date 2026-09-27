namespace FileSorter.Core;

/// <summary>
/// How the application was launched, parsed from command-line args.
/// </summary>
public enum StartupMode
{
    /// <summary>No args — normal tray-app launch.</summary>
    Normal,
    /// <summary><c>--sendto &lt;files...&gt;</c> — Explorer "Send To" entry point.</summary>
    SendTo,
    /// <summary><c>--install</c> — register Run-key autostart and exit.</summary>
    Install,
    /// <summary><c>--uninstall</c> — remove Run-key autostart and exit.</summary>
    Uninstall,
    /// <summary><c>--install-sendto</c> — create the SendTo .lnk shortcut and exit.</summary>
    InstallSendTo,
}

/// <summary>
/// Parsed command-line args. <see cref="Files"/> is non-empty only when <see cref="Mode"/> is <see cref="StartupMode.SendTo"/>.
/// </summary>
public record StartupArgs(StartupMode Mode, IReadOnlyList<string> Files)
{
    public static StartupArgs Parse(string[] args)
    {
        if (args == null || args.Length == 0)
            return new StartupArgs(StartupMode.Normal, Array.Empty<string>());

        switch (args[0])
        {
            case "--sendto":
                return new StartupArgs(StartupMode.SendTo, args.Skip(1).ToList());
            case "--install":
                return new StartupArgs(StartupMode.Install, Array.Empty<string>());
            case "--uninstall":
                return new StartupArgs(StartupMode.Uninstall, Array.Empty<string>());
            case "--install-sendto":
                return new StartupArgs(StartupMode.InstallSendTo, Array.Empty<string>());
            default:
                return new StartupArgs(StartupMode.Normal, Array.Empty<string>());
        }
    }
}