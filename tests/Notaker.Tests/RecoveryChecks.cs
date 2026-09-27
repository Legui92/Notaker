using System.IO;
using System.Text;
using NAudio.Wave;
using Notaker;

static class RecoveryChecks
{
    public static void Run(string root)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
        var data = Path.Combine(root, "recovery-tests");
        var store = new RecoveryStore(data);
        var pcm = new byte[32000]; new Random(123).NextBytes(pcm);
        string id;
        using (var capture = store.Begin())
        {
            id = capture.Id;
            for (int i = 0; i < 173; i++) capture.Append(pcm, pcm.Length);
        }
        var reopened = new RecoveryStore(data);
        var wav = reopened.ReadAudio(id);
        using (var reader = new WaveFileReader(new MemoryStream(wav)))
        {
            Check(reader.TotalTime.TotalSeconds == 173, "A 2m53s recording survives store restart with exact duration");
            var result = new byte[pcm.Length]; reader.Read(result, 0, result.Length);
            Check(result.SequenceEqual(pcm), "Encrypted audio round trip preserves original PCM");
        }
        var encryptedPath = Path.Combine(data, "recovery", id + ".audio");
        Check(File.ReadAllBytes(encryptedPath).AsSpan().IndexOf(pcm.AsSpan()) < 0, "Recovery file does not expose raw PCM");
        store.SaveText(id, "PRIVATE TEST TEXT no real dictation");
        Check(reopened.ReadText(id) == "PRIVATE TEST TEXT no real dictation", "Recognized text survives restart separately from audio");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.ChangeExtension(encryptedPath, ".text"))).Contains("PRIVATE TEST TEXT"), "Recovered text is encrypted at rest");
        using (var file = new FileStream(encryptedPath, FileMode.Append, FileAccess.Write))
        { using var writer = new BinaryWriter(file); writer.Write(500); writer.Write(new byte[100]); }
        Check(store.ReadAudio(id).SequenceEqual(wav), "Interrupted final encrypted chunk preserves preceding complete audio");
        string other;
        using (var capture = store.Begin()) { other = capture.Id; capture.Append(pcm, pcm.Length); }
        store.Delete(id);
        Check(store.Entries().Count == 1 && store.Entries()[0].Id == other, "Deleting one recovery preserves other pending dictations");
        Check(!File.Exists(Path.ChangeExtension(encryptedPath, ".text")), "Recovery deletion removes encrypted text too");
        bool invalid = false;
        try { store.Delete("../settings"); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "Recovery IDs cannot escape the recovery folder");
        var dependencies = Path.Combine(root, "bundle-fixture"); Directory.CreateDirectory(dependencies);
        var dll = Path.Combine(dependencies, "Accessibility.dll"); File.WriteAllText(dll, "fixture");
        using (new BundleLease(dependencies))
        {
            bool blocked = false; try { File.Delete(dll); } catch (IOException) { blocked = true; }
            Check(blocked && File.ReadAllText(dll) == "fixture", "Temporary cleanup cannot delete dependencies while app lease is active");
        }
        File.Delete(dll); Check(!File.Exists(dll), "Closing application releases dependency locks");
        AppLog.Configure(root);
        AppLog.Write("test.failure", new InvalidOperationException("PRIVATE TEST TEXT secret-api-key"));
        var log = File.ReadAllText(Path.Combine(root, "logs", "notaker.log"));
        Check(log.Contains("InvalidOperationException") && !log.Contains("PRIVATE TEST TEXT") && !log.Contains("secret-api-key"), "Diagnostics retain exception type without text or secret messages");
        var childData = Path.Combine(root, "killed-recorder");
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);
        start.ArgumentList.Add("--recovery-crash-fixture"); start.ArgumentList.Add(childData);
        using (var child = System.Diagnostics.Process.Start(start)!)
        {
            try
            {
                var childId = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult()!;
                child.Kill(entireProcessTree: true); child.WaitForExit();
                using var restored = new WaveFileReader(new MemoryStream(new RecoveryStore(childData).ReadAudio(childId)));
                Check(restored.TotalTime.TotalSeconds == 1, "Audio survives force-killing recording process without Dispose or StopAsync");
            }
            finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
        }
        var storage = new Storage(Path.Combine(root, "dedup"));
        var dictation = new Dictation("test", DateTime.Now, 1) { RecoveryId = other };
        storage.Add(dictation); storage.Add(dictation);
        Check(storage.History.Count == 1 && storage.Statistics.Data.Dictations == 1, "Retry after persisted history cannot duplicate dictation or statistics");
    }
}
