using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using Notaker;
using Whisper.net;

internal static class AttentionBenchmark
{
    public static async Task Run(string model, string fixtureFolder, string output)
    {
        Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan];
        var fixtures = new Dictionary<string, byte[]>();
        int repetitions = 0;
        foreach (var name in new[] { "english", "spanish", "mixed" })
            fixtures[name] = await File.ReadAllBytesAsync(Path.Combine(fixtureFolder, name + ".wav"));
        using (var reader = new WaveFileReader(new MemoryStream(fixtures["mixed"])))
        using (var buffer = new MemoryStream())
        {
            var pcm = new byte[reader.Length]; reader.ReadExactly(pcm);
            using var writer = new WaveFileWriter(buffer, reader.WaveFormat);
            while (writer.TotalTime.TotalSeconds < 120) { writer.Write(pcm); writer.Write(new byte[reader.WaveFormat.AverageBytesPerSecond]); repetitions++; }
            writer.Flush(); fixtures["long"] = buffer.ToArray();
        }
        var results = new List<object>();
        var baseline = new Dictionary<string, string>();
        var reference = string.Join(" ", Enumerable.Repeat("Necesito revisar el pull request antes de hacer el deployment mañana. Comprueba que pasen las pruebas del proyecto.", repetitions));
        Console.WriteLine($"Long reference repetitions={repetitions}");
        // Balance order across rounds. Preparation is not included in inference.
        for (int round = 0; round < 3; round++)
        foreach (var flash in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            using var factory = WhisperFactory.FromPath(model, new WhisperFactoryOptions { UseGpu = true, UseFlashAttention = flash });
            using (var validation = factory.CreateBuilder().Build()) { }
            foreach (var fixture in fixtures)
            {
                var watch = Stopwatch.StartNew();
                var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                var builder = factory.CreateBuilder().WithLanguage("auto").WithTemperature(0).WithPrompt("Español e inglés. Spanish and English. ");
                builder.WithBeamSearchSamplingStrategy();
                using var processor = builder.Build();
                using var stream = new MemoryStream(fixture.Value);
                var text = new StringBuilder();
                await foreach (var segment in processor.ProcessAsync(stream))
                { if (text.Length > 0) text.Append(' '); text.Append(segment.Text.Trim()); }
                watch.Stop();
                var clean = DictationText.CleanArtifacts(text.ToString());
                if (!baseline.ContainsKey(fixture.Key)) baseline[fixture.Key] = clean;
                var edits = TextMetrics.WordEdits(baseline[fixture.Key], clean);
                results.Add(new { round, flash, fixture = fixture.Key, seconds = watch.Elapsed.TotalSeconds,
                    cpuSeconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalSeconds, wordEdits = edits,
                    referenceEdits = fixture.Key == "long" ? TextMetrics.WordEdits(reference, clean) : (int?)null, text = clean });
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{fixture.Key} round={round} flash={flash} time={watch.Elapsed.TotalSeconds:F3}s edits={edits}");
            }
        }
    }
}
