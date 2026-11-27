using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.Wave;
using Notaker;

/// <summary>
/// Accuracy bench on local recordings: WER and vocabulary term hits per block and in total, for batch and live
/// recognition with and without the previous fragment as context. Console output has counts only; recognized
/// texts go to OUTPUT, which must live outside the repository. No network, microphone or AI editor.
/// Usage: --voice-eval AUDIO_DIR REFERENCE VOCAB MODEL OUTPUT LANGUAGE [TERMS]
/// REFERENCE: "Bloque N" headers, each followed by its text; audio is AUDIO_DIR/bloqueN.wav (16 kHz mono).
/// VOCAB: vocabulary.json (JSON array) or one entry per line. TERMS: optional extra jargon, one per line.
/// </summary>
internal static class VoiceEval
{
    internal sealed record Score(int Words, int Edits, int Expected, int Hits, int Repeats, List<string> Missed)
    {
        public double Wer => Words == 0 ? 0 : (double)Edits / Words;
        public static Score Empty => new(0, 0, 0, 0, 0, []);
        public static Score operator +(Score a, Score b) => new(a.Words + b.Words, a.Edits + b.Edits, a.Expected + b.Expected, a.Hits + b.Hits, a.Repeats + b.Repeats, [.. a.Missed, .. b.Missed]);
        public override string ToString() => $"WER={Wer:P1} ({Edits}/{Words}) terms={Hits}/{Expected} repeats={Repeats}";
    }

    public static List<(int Block, string Text)> ParseReference(string text)
    {
        var blocks = new List<(int, string)>();
        int? current = null; var lines = new List<string>();
        void Flush() { if (current is int n && lines.Count > 0) blocks.Add((n, string.Join(" ", lines))); lines.Clear(); }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var header = Regex.Match(line, @"^Bloque\s+(\d+)$", RegexOptions.IgnoreCase);
            if (header.Success) { Flush(); current = int.Parse(header.Groups[1].Value); continue; }
            if (line.Length == 0) { Flush(); current = null; continue; }
            if (current != null) lines.Add(line);
        }
        Flush();
        return blocks;
    }

    // Lowercase probes ignore case (sentence starts); probes with capitals must keep their exact spelling.
    // Hyphens count as part of a word, so "Nova-Hub" inside "Nova-Hub-Alerts" is not a hit.
    public static int Occurrences(string text, string probe)
    {
        var pattern = @"(?<![\p{L}\p{M}\p{N}-])" + Regex.Escape(probe) + @"(?![\p{L}\p{M}\p{N}]|-[\p{L}\p{N}])";
        return Regex.Matches(text, pattern, probe.Any(char.IsUpper) ? RegexOptions.None : RegexOptions.IgnoreCase).Count;
    }

    // Word trigrams the hypothesis has more often than the reference: loops and echoed prompt text.
    public static int Repeats(string reference, string hypothesis)
    {
        static Dictionary<string, int> Grams(string text)
        {
            var words = Regex.Matches(text.ToUpperInvariant(), @"[\p{L}\p{M}\p{N}]+").Select(m => m.Value).ToArray();
            var grams = new Dictionary<string, int>();
            for (var i = 0; i + 2 < words.Length; i++) { var g = $"{words[i]} {words[i + 1]} {words[i + 2]}"; grams[g] = grams.GetValueOrDefault(g) + 1; }
            return grams;
        }
        var r = Grams(reference);
        return Grams(hypothesis).Sum(g => Math.Max(0, g.Value - Math.Max(1, r.GetValueOrDefault(g.Key))));
    }

    public static Score Measure(string reference, string hypothesis, IReadOnlyList<string> probes)
    {
        int expected = 0, hits = 0; var missed = new List<string>();
        foreach (var probe in probes)
        {
            var want = Occurrences(reference, probe);
            if (want == 0) continue;
            var got = Math.Min(want, Occurrences(hypothesis, probe));
            expected += want; hits += got;
            for (var i = got; i < want; i++) missed.Add(probe);
        }
        return new(TextMetrics.WordCount(reference), TextMetrics.WordEdits(reference, hypothesis), expected, hits, Repeats(reference, hypothesis), missed);
    }

    public static List<string> ReadVocabulary(string path)
    {
        var text = File.ReadAllText(path);
        List<string> lines = text.TrimStart().StartsWith('[') ? JsonSerializer.Deserialize<List<string>>(text) ?? [] : [.. text.Split('\n')];
        return PersonalVocabulary.Normalize(lines);
    }

    public static async Task Run(string audioDir, string referencePath, string vocabularyPath, string model, string output, string language, string? termsPath)
    {
        Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan];
        logRoot = Path.Combine(Path.GetTempPath(), "Notaker-tests-voice-eval-" + Guid.NewGuid());
        AppLog.Configure(logRoot);
        var blocks = ParseReference(File.ReadAllText(referencePath));
        if (blocks.Count == 0) throw new InvalidDataException("Reference has no 'Bloque N' sections");
        var vocabulary = ReadVocabulary(vocabularyPath);
        IEnumerable<string> extra = termsPath == null ? [] : File.ReadAllLines(termsPath).Select(l => l.Trim()).Where(l => l.Length > 0);
        var probes = PersonalVocabulary.Entries(vocabulary).Select(e => e.Term).Concat(extra).Distinct().ToList();
        var audio = blocks.Select(b => File.ReadAllBytes(Path.Combine(audioDir, $"bloque{b.Block}.wav"))).ToList();
        var glossary = PersonalVocabulary.Prompt(vocabulary);
        using var engine = new Transcriber();
        await engine.PrepareAsync(model, true, CancellationToken.None);
        Console.WriteLine($"model={Path.GetFileName(model)} backend={Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary} language={language} blocks={blocks.Count} probes={probes.Count} glossaryChars={glossary.Length}");
        foreach (var (block, wav) in blocks.Zip(audio)) Console.WriteLine($"audio bloque{block.Block}: {AudioStats(Pcm(wav))}");
        var report = new List<object>();
        var summary = new List<(string Config, Score Total)>();

        void Finish(string config, Score total, Stopwatch watch, List<object> rows)
        {
            Console.WriteLine($"{config} TOTAL: {total} seconds={watch.Elapsed.TotalSeconds:F1}");
            summary.Add((config, total));
            report.Add(new { config, total.Wer, total.Edits, total.Words, total.Hits, total.Expected, total.Repeats, total.Missed, seconds = watch.Elapsed.TotalSeconds, rows });
        }

        async Task Batch(string config, bool useGlossary, bool apply, bool context)
        {
            var total = Score.Empty; var previous = ""; var watch = Stopwatch.StartNew(); var rows = new List<object>();
            foreach (var (block, wav) in blocks.Zip(audio))
            {
                var prompt = !useGlossary ? null : context ? PersonalVocabulary.LivePrompt(vocabulary, previous) : glossary;
                var raw = await engine.TranscribeAsync(model, wav, language, CancellationToken.None, prompt);
                previous = previous.Length == 0 ? raw : previous + " " + raw;
                var text = apply ? PersonalVocabulary.Apply(raw, vocabulary) : raw;
                var score = Measure(block.Text, text, probes); total += score;
                Console.WriteLine($"{config} bloque{block.Block}: {score}");
                rows.Add(new { block = block.Block, prompt, raw, text, score.Wer, score.Edits, score.Words, score.Hits, score.Expected, score.Repeats, score.Missed });
            }
            Finish(config, total, watch, rows);
        }

        byte[] Session()
        {
            var session = new MemoryStream();
            foreach (var wav in audio) { session.Write(Pcm(wav)); session.Write(new byte[48000]); }
            return session.ToArray();
        }

        async Task Live(string config, int chunkSeconds, bool context, byte[]? tail = null, string tailReference = "")
        {
            // One recording: blocks in order with 1.5 s of silence between them, fed in real time like the recorder.
            // An optional quiet tail follows, below the speech threshold in every 100 ms frame.
            var pcm = tail == null ? Session() : [.. Session(), .. tail];
            var reference = string.Join(" ", blocks.Select(b => b.Text)) + (tailReference.Length > 0 ? " " + tailReference : "");
            var prompts = new List<string>();
            var watch = Stopwatch.StartNew();
            await using var live = new LiveTranscription((wav, previous, token) =>
            {
                var prompt = context ? PersonalVocabulary.LivePrompt(vocabulary, previous) : glossary;
                lock (prompts) prompts.Add(prompt);
                return engine.TranscribeAsync(model, wav, language, token, prompt);
            }, CancellationToken.None, chunkSeconds);
            for (int offset = 0; offset < pcm.Length; offset += 3200)
            {
                var chunk = pcm.AsSpan(offset, Math.Min(3200, pcm.Length - offset)).ToArray();
                live.Append(chunk, chunk.Length, Rms(chunk));
                var due = TimeSpan.FromSeconds((offset + chunk.Length) / 32000.0) - watch.Elapsed;
                if (due > TimeSpan.Zero) await Task.Delay(due);
            }
            var wait = Stopwatch.StartNew();
            var raw = await live.CompleteAsync();
            wait.Stop();
            var text = raw == null ? "" : PersonalVocabulary.Apply(raw, vocabulary);
            var score = Measure(reference, text, probes);
            var reason = live.FellBack ? string.Join(",", LogStages().Where(s => s.StartsWith("live.")).TakeLast(1)) : "";
            Console.WriteLine($"{config} session: chunks={live.CompletedChunks} fallback={live.FellBack} finalWait={wait.Elapsed.TotalSeconds:F1}s {reason}");
            Finish(config, score, watch, [new { chunks = live.CompletedChunks, fellBack = live.FellBack, prompts, raw, text, score.Wer, score.Edits, score.Words, score.Hits, score.Expected, score.Repeats, score.Missed }]);
        }

        async Task Fragments(string config, int chunkSeconds, bool context)
        {
            // Same pause rule as live mode, decided offline: no backlog or quiet-tail fallback, so every cut is measured.
            var pcm = Session();
            var reference = string.Join(" ", blocks.Select(b => b.Text));
            var watch = Stopwatch.StartNew(); var prompts = new List<string>(); var previous = "";
            foreach (var segment in Segments(pcm, chunkSeconds))
            {
                using var wav = new MemoryStream();
                using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1))) writer.Write(segment);
                var prompt = context ? PersonalVocabulary.LivePrompt(vocabulary, previous) : glossary;
                prompts.Add(prompt);
                var part = (await engine.TranscribeAsync(model, wav.ToArray(), language, CancellationToken.None, prompt)).Trim();
                previous = previous.Length == 0 ? part : previous + " " + part;
            }
            var text = PersonalVocabulary.Apply(previous, vocabulary);
            var score = Measure(reference, text, probes);
            Console.WriteLine($"{config} session: fragments={prompts.Count}");
            Finish(config, score, watch, [new { fragments = prompts.Count, prompts, raw = previous, text, score.Wer, score.Edits, score.Words, score.Hits, score.Expected, score.Repeats, score.Missed }]);
        }

        await Batch("a-plain", useGlossary: false, apply: false, context: false);
        await Batch("b-glossary-apply", useGlossary: true, apply: true, context: false);
        await Batch("c-blocks-as-fragments-context", useGlossary: true, apply: true, context: true);
        await Live("live30-no-context", 30, context: false);
        await Live("live30-context", 30, context: true);
        // Quiet final tail after the last cut (Ph3): room noise from these recordings, and the first block spoken softly.
        var session = Session();
        var noise = NoiseTail(audio.Select(Pcm), 3);
        var soft = Soften(Pcm(audio[0]));
        var tailChunk = TailCut(session, soft.Length);
        Console.WriteLine($"tail noise: {AudioStats(noise)}");
        Console.WriteLine($"tail soft: {AudioStats(soft)} cutSeconds={tailChunk}");
        // Same cut length without a tail: fragment boundaries alone change a few words, so compare tails against this.
        await Live($"live{tailChunk}-no-tail", tailChunk, context: false);
        await Live("live-tail-noise", tailChunk, context: false, noise);
        await Live("live-tail-soft", tailChunk, context: false, soft, blocks[0].Text);
        foreach (var seconds in new[] { 6, 12 })
        {
            await Fragments($"frag{seconds}-no-context", seconds, context: false);
            await Fragments($"frag{seconds}-context", seconds, context: true);
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { model = Path.GetFileName(model), language, report }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("SUMMARY");
        foreach (var (config, total) in summary) Console.WriteLine($"{config}: {total}");
        try { Directory.Delete(logRoot, true); } catch (IOException) { }
    }

    private static string logRoot = "";
    // Structural log stages only (the app log never holds dictated text).
    private static IEnumerable<string> LogStages()
    {
        var path = Path.Combine(logRoot, "logs", "notaker.log");
        if (!File.Exists(path)) return [];
        return File.ReadAllLines(path).Select(l => { using var json = JsonDocument.Parse(l); return json.RootElement.GetProperty("stage").GetString() ?? ""; }).ToList();
    }

    private static byte[] Pcm(byte[] wav)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
            throw new InvalidDataException("16 kHz mono 16-bit WAV required");
        var pcm = new byte[reader.Length]; reader.ReadExactly(pcm); return pcm;
    }

    /// <summary>Cuts after at least <paramref name="minimumSeconds"/> at a 0.7 s pause; a remainder without speech joins the last piece.</summary>
    public static List<byte[]> Segments(byte[] pcm, int minimumSeconds)
    {
        var pieces = new List<byte[]>(); var pending = new MemoryStream(); int silent = 0; bool speech = false;
        for (int offset = 0; offset < pcm.Length; offset += 3200)
        {
            var frame = pcm.AsSpan(offset, Math.Min(3200, pcm.Length - offset)).ToArray();
            pending.Write(frame);
            if (Rms(frame) > 0.006f) { speech = true; silent = 0; } else silent += frame.Length;
            if (pending.Length >= minimumSeconds * 32000 && silent >= 22400) { pieces.Add(pending.ToArray()); pending.SetLength(0); speech = false; silent = 0; }
        }
        if (pending.Length > 0)
        {
            if (speech || pieces.Count == 0) pieces.Add(pending.ToArray());
            else pieces[^1] = [.. pieces[^1], .. pending.ToArray()];
        }
        return pieces;
    }

    /// <summary>Nonzero frames at or below the speech threshold, in recording order, up to <paramref name="seconds"/>.</summary>
    public static byte[] NoiseTail(IEnumerable<byte[]> recordings, int seconds)
    {
        var tail = new MemoryStream();
        foreach (var pcm in recordings)
            for (int offset = 0; offset + 3200 <= pcm.Length && tail.Length < seconds * 32000; offset += 3200)
            {
                var frame = pcm.AsSpan(offset, 3200).ToArray();
                var level = Rms(frame);
                if (level > 0 && level <= 0.005f) tail.Write(frame);
            }
        return tail.ToArray();
    }

    /// <summary>Scales speech so its loudest 100 ms frame sits just under the 0.006 RMS speech threshold.</summary>
    public static byte[] Soften(byte[] pcm)
    {
        var loudest = Enumerable.Range(0, pcm.Length / 3200).Max(i => Rms(pcm.AsSpan(i * 3200, 3200).ToArray()));
        var gain = 0.0055 / Math.Max(loudest, 1e-6);
        var output = new byte[pcm.Length];
        for (int j = 0; j + 1 < pcm.Length; j += 2)
            BitConverter.TryWriteBytes(output.AsSpan(j, 2), (short)Math.Round(BitConverter.ToInt16(pcm, j) * gain));
        return output;
    }

    /// <summary>Smallest live cut length that lands the last cut in the session's trailing pause and leaves the tail uncut.</summary>
    public static int TailCut(byte[] session, int tailBytes)
    {
        for (var seconds = tailBytes / 32000 + 2; seconds <= 60; seconds++)
            if (LastCutAtEnd(session, seconds)) return seconds;
        throw new InvalidDataException("No cut length isolates the tail");
    }

    private static bool LastCutAtEnd(byte[] session, int minimumSeconds)
    {
        // Mirrors LiveTranscription.Append: the last cut must happen within the final 1.5 s of silence.
        int silent = 0; long pending = 0; long lastCut = -1;
        for (int offset = 0; offset < session.Length; offset += 3200)
        {
            var frame = session.AsSpan(offset, Math.Min(3200, session.Length - offset)).ToArray();
            pending += frame.Length;
            if (Rms(frame) > 0.006f) silent = 0; else silent += frame.Length;
            if (pending >= minimumSeconds * 32000 && silent >= 22400) { lastCut = offset + frame.Length; pending = 0; silent = 0; }
        }
        return lastCut >= session.Length - 48000;
    }

    private static float Rms(byte[] chunk)
    {
        double squares = 0;
        for (int j = 0; j + 1 < chunk.Length; j += 2) squares += Math.Pow(BitConverter.ToInt16(chunk, j) / 32768.0, 2);
        return (float)Math.Sqrt(squares / Math.Max(1, chunk.Length / 2));
    }

    // Recording health with the same 100 ms frames and 0.006 RMS speech threshold as live segmentation.
    private static string AudioStats(byte[] pcm)
    {
        var frames = Enumerable.Range(0, pcm.Length / 3200).Select(i => Rms(pcm.AsSpan(i * 3200, 3200).ToArray())).ToArray();
        int peak = 0; for (int j = 0; j + 1 < pcm.Length; j += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, j)));
        int longest = 0, run = 0; foreach (var f in frames) { run = f <= 0.006f ? run + 1 : 0; longest = Math.Max(longest, run); }
        var speech = frames.Where(f => f > 0.006f).DefaultIfEmpty(0).Order().ToArray();
        var sorted = frames.Order().ToArray();
        return $"seconds={pcm.Length / 32000.0:F1} peak={peak / 32768.0:F3} speechRmsMedian={speech[speech.Length / 2]:F4} quietFrames={frames.Count(f => f <= 0.006f) * 100 / Math.Max(1, frames.Length)}% longestQuiet={longest / 10.0:F1}s noiseFloorP10={sorted[sorted.Length / 10]:F4}";
    }
}
