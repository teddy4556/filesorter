using System.Runtime.InteropServices;

namespace FileSorter.Core;

/// <summary>
/// Manages the Explorer "Send To" shortcut that lets users right-click any file
/// → Send to → FileSorter to trigger classification.
/// </summary>
/// <remarks>
/// Uses the WScript.Shell COM interface to write a real .lnk file. Windows-only.
/// </remarks>
public static class SendToInstaller
{
    public static string GetSendToDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "SendTo");
    }

    public static string GetShortcutPath(string exePath)
    {
        return Path.Combine(GetSendToDir(), "FileSorter.lnk");
    }

    /// <summary>
    /// True when the FileSorter.lnk shortcut already exists in the SendTo folder.
    /// </summary>
    public static bool IsInstalled(string exePath, string? sendToDir = null)
    {
        sendToDir ??= GetSendToDir();
        return File.Exists(Path.Combine(sendToDir, "FileSorter.lnk"));
    }

    /// <summary>
    /// Create a .lnk in the SendTo folder pointing to <paramref name="exePath"/>.
    /// Arguments are preset to <c>--sendto</c> and WindowStyle to 7 (minimized).
    /// </summary>
    public static void Install(string exePath)
    {
        var comType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM not available (Windows-only).");
        // Cast to dynamic so we can call COM members (CreateShortcut, Save) without
        // referencing Interop.IWshRuntimeLibrary.
        dynamic shell = Activator.CreateInstance(comType)
            ?? throw new InvalidOperationException("Failed to instantiate WScript.Shell.");

        try
        {
            dynamic shortcut = shell.CreateShortcut(GetShortcutPath(exePath));
            try
            {
                shortcut.TargetPath = exePath;
                shortcut.Arguments = "--sendto";
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
                shortcut.WindowStyle = 7;  // 7 = Minimized
                shortcut.Save();
            }
            finally
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            if (Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Delete the FileSorter.lnk from the SendTo folder. No-op if not installed.
    /// Returns true if a file was deleted.
    /// </summary>
    public static bool Uninstall(string? sendToDir = null)
    {
        sendToDir ??= GetSendToDir();
        var lnk = Path.Combine(sendToDir, "FileSorter.lnk");
        if (!File.Exists(lnk)) return false;
        File.Delete(lnk);
        return true;
    }
}