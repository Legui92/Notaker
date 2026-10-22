using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Notaker;

internal static class LiveBenchmark
{
    public static async Task Run(string model, string fixture, string output)
    {
        Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan];
        // Only use public/synthesized fixtures. No API calls, microphone or private history.
        using var reader = new WaveFileReader(fixture);
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels != 1) throw new InvalidDataException("Mono fixture required");
        var source = new SampleToWaveProvider16(new WdlResamplingSampleProvider(samples, 16000));
        using var block = new MemoryStream();
        var buffer = new byte[3200];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) block.Write(buffer, 0, read);
        block.Write(new byte[32000]);
        using var audio = new MemoryStream();
        while (audio.Length < 240 * 32000) audio.Write(block.ToArray());
        audio.SetLength(240 * 32000);
        var pcm = audio.ToArray();
        using var wav = new MemoryStream();
        using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1)))
        {
            writer.Write(pcm); writer.Flush();
            var fullWav = wav.ToArray();
            using var engine = new Transcriber();
            await engine.PrepareAsync(model, true, CancellationToken.None);
            Console.WriteLine("Backend: " + Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary);
            var baseline = new List<double>();
            string original = "";
            for (int i = 0; i < 3; i++)
            {
                var watch = Stopwatch.StartNew();
                original = await engine.TranscribeAsync(model, fullWav, "mixed", CancellationToken.None);
                baseline.Add(watch.Elapsed.TotalSeconds);
                Console.WriteLine($"Baseline {i + 1}: {watch.Elapsed.TotalSeconds:F2}s");
            }
            var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
            var capture = Stopwatch.StartNew();
            await using var live = new LiveTranscription((chunk, token) => engine.TranscribeAsync(model, chunk, "mixed", token), CancellationToken.None);
            for (int offset = 0; offset < pcm.Length; offset += 3200)
            {
                var chunk = pcm.AsSpan(offset, Math.Min(3200, pcm.Length - offset)).ToArray();
                double squares = 0;
                for (int j = 0; j < chunk.Length; j += 2) squares += Math.Pow(BitConverter.ToInt16(chunk, j) / 32768.0, 2);
                live.Append(chunk, chunk.Length, (float)Math.Sqrt(squares / (chunk.Length / 2)));
                var remaining = TimeSpan.FromSeconds((offset + chunk.Length) / 32000.0) - capture.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            }
            var tail = Stopwatch.StartNew();
            var result = await live.CompleteAsync();
            tail.Stop();
            var report = new { backend = Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary.ToString(), audioSeconds = 240, baselineSeconds = baseline, finalWaitSeconds = tail.Elapsed.TotalSeconds,
                live.CompletedChunks, live.FellBack, wordEditsAgainstBatch = TextMetrics.WordEdits(original, result ?? ""),
                processCpuSecondsDuringLive = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalSeconds,
                original, liveText = result };
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Live final wait: {tail.Elapsed.TotalSeconds:F2}s; chunks={live.CompletedChunks}; fallback={live.FellBack}");
            if (result == null) throw new Exception("Live benchmark fell back");
        }
    }
}
