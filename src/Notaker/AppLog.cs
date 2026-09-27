using System.IO;
using System.Text.Json;

namespace Notaker;

public static class AppLog
{
    private static readonly object Gate = new();
    private static string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notaker");
    public static void Configure(string path) => root = path;
    public static void Write(string stage, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                var folder = Path.Combine(root, "logs"); Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "notaker.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".previous", true);
                // Exception messages may contain dictated text or credentials. Only structural diagnostics.
                File.AppendAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, version = UpdateService.VersionLabel, stage,
                    type = error?.GetType().FullName, hresult = error?.HResult,
                    frames = error == null ? null : new System.Diagnostics.StackTrace(error, false).GetFrames()?.Select(f => f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name).ToArray() }) + Environment.NewLine);
            }
        }
        catch { /* Logging must never turn a recoverable error into an application crash. */ }
    }
}

public sealed class BundleLease : IDisposable
{
    private readonly List<FileStream> files = [];
    public BundleLease(string directory)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                files.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        }
        catch { Dispose(); throw; }
    }
    public static BundleLease? Acquire()
    {
        var paths = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "").Split(Path.PathSeparator);
        var folder = paths.FirstOrDefault(p => File.Exists(Path.Combine(p, "Notaker.dll")) && Directory.Exists(Path.Combine(p, "runtimes")));
        return folder == null ? null : new BundleLease(folder);
    }
    public void Dispose() { foreach (var file in files) file.Dispose(); files.Clear(); }
}
