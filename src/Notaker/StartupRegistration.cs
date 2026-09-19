using System.IO;
using Microsoft.Win32;

namespace Notaker;

public record StartupStatus(bool Registered, bool DisabledByWindows, bool CurrentExecutable)
{
    public bool Enabled => Registered && !DisabledByWindows;
}

/// <summary>Per-user login registration. Windows remains the owner of StartupApproved.</summary>
public sealed class StartupRegistration
{
    private readonly string executable;
    private readonly string runKey;
    private readonly string approvedKey;
    public StartupRegistration(string executable, string registryRoot = @"Software\Microsoft\Windows\CurrentVersion")
    {
        this.executable = Path.GetFullPath(executable);
        runKey = registryRoot + @"\Run";
        approvedKey = registryRoot + @"\Explorer\StartupApproved\Run";
    }

    public StartupStatus Read()
    {
        using var run = Registry.CurrentUser.OpenSubKey(runKey);
        var command = run?.GetValue("Notaker") as string;
        using var approved = Registry.CurrentUser.OpenSubKey(approvedKey);
        var state = approved?.GetValue("Notaker") as byte[];
        // Windows currently uses 3/7 for disabled entries. Read only: never reset a user's veto.
        var disabled = state is { Length: >= 4 } && (BitConverter.ToUInt32(state, 0) is 3 or 7);
        return new(!string.IsNullOrWhiteSpace(command), disabled,
            string.Equals(command, Command, StringComparison.OrdinalIgnoreCase));
    }

    private string Command => $"\"{executable}\" --startup";

    public void SetRegistered(bool enabled)
    {
        if (enabled)
        {
            if (!File.Exists(executable) || !string.Equals(Path.GetFileName(executable), "Notaker.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Activa el inicio desde el ejecutable Notaker.exe que quieras conservar.");
            if (Command.Length > 260 || executable.Contains('"'))
                throw new InvalidOperationException("Mueve Notaker.exe a una ruta más corta antes de activar el inicio.");
            using var key = Registry.CurrentUser.CreateSubKey(runKey);
            key.SetValue("Notaker", Command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            key?.DeleteValue("Notaker", throwOnMissingValue: false);
        }
    }
}
