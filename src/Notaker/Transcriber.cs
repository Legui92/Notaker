using System.IO;
using System.Net.Http;
using System.Text;
using Whisper.net;

namespace Notaker;

public sealed class Transcriber : IDisposable
{
    static Transcriber()
    {
        // Single-file .NET apps extract native DLLs outside AppContext.BaseDirectory.
        var paths = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "").Split(Path.PathSeparator);
        foreach (var directory in paths)
        {
            if (File.Exists(Path.Combine(directory, "runtimes", "win-x64", "whisper.dll")))
            {
                Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = Path.Combine(directory, "Notaker.dll");
                break;
            }
        }
    }
    private WhisperFactory? factory;
    private string? loadedModel;
    private bool loadedUseGpu;
    public static bool ModelExists(string path)
    {
        var model = VoiceModels.All.FirstOrDefault(m => Path.GetFileName(path) == $"ggml-{m.Id}.bin");
        return File.Exists(path) && new FileInfo(path).Length >= (model?.MinimumBytes ?? 100_000_000);
    }

    public static async Task DownloadModelAsync(string path, string model, IProgress<double> progress, CancellationToken token)
    {
        if (!VoiceModels.IsSupported(model)) throw new ArgumentException("Modelo no admitido.", nameof(model));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".download";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
            using var response = await http.GetAsync($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{model}.bin", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var destination = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), token);
                    total += read;
                    progress.Report(length > 0 ? total * 100.0 / length.Value : -1);
                }
                if (total < VoiceModels.Get(model).MinimumBytes || (length.HasValue && total != length.Value))
                    throw new IOException("La descarga del modelo está incompleta. Vuelve a intentarlo.");
            }
            // Validate with the native reader before making the download available.
            using (WhisperFactory.FromPath(temp, new WhisperFactoryOptions { UseGpu = false })) { }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public Task<string> TranscribeAsync(string model, byte[] wav, string language, CancellationToken token, string? vocabulary = null, bool useGpu = true) => Task.Run(async () =>
    {
        if (loadedModel != model || loadedUseGpu != useGpu) { factory?.Dispose(); factory = null; loadedModel = model; loadedUseGpu = useGpu; }
        factory ??= WhisperFactory.FromPath(model, new WhisperFactoryOptions { UseGpu = useGpu });
        var builder = factory.CreateBuilder().WithLanguage(language == "mixed" ? "auto" : language)
            .WithTemperature(0);
        builder.WithBeamSearchSamplingStrategy();
        var prompt = language == "mixed" ? "Español e inglés. Spanish and English. " + vocabulary : vocabulary;
        if (!string.IsNullOrWhiteSpace(prompt)) builder.WithPrompt(prompt);
        using var processor = builder.Build();
        using var stream = new MemoryStream(wav);
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(stream, token))
        {
            var part = segment.Text.Trim();
            if (part.Length > 0) { if (text.Length > 0) text.Append(' '); text.Append(part); }
        }
        return DictationText.CleanArtifacts(text.ToString());
    }, token);

    public void Dispose() => factory?.Dispose();
}
