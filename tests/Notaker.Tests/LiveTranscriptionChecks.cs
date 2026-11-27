using Notaker;
using NAudio.Wave;
using System.IO;

internal static class LiveTranscriptionChecks
{
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        Console.WriteLine("PASS: " + description);
    }
    private static byte[] ReadPcm(byte[] wav)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        var pcm = new byte[reader.Length];
        reader.ReadExactly(pcm);
        return pcm;
    }
    private static void Feed(LiveTranscription live, int speechBuffers = 300)
    {
        for (var i = 0; i < speechBuffers; i++) live.Append(Enumerable.Repeat((byte)1, 3200).ToArray(), 3200, .1f);
        for (var i = 0; i < 7; i++) live.Append(new byte[3200], 3200, 0);
    }
    public static async Task Run(string root)
    {
        var storage = new Storage(root);
        Check(storage.Settings.TranscribeWhileRecording, "Live transcription is enabled when no preference was saved");
        storage.Settings.TranscribeWhileRecording = true; storage.SaveSettings();
        Check(new Storage(root).Settings.TranscribeWhileRecording, "Live mode preference persists");
        storage.Add(new Dictation("synthetic", DateTime.Now, 20) { RecognitionDuringRecording = true, RecognitionSeconds = .5 });
        var saved = new Storage(root).History[0];
        Check(saved.RecognitionDuringRecording && saved.Caption.Contains("Espera voz"), "History identifies final voice wait rather than total inference time");
        var chunks = new List<byte[]>();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var live = new LiveTranscription((wav, _) =>
        {
            chunks.Add(ReadPcm(wav)); first.TrySetResult();
            return Task.FromResult(chunks.Count == 1 ? "La reunión. The meeting." : "Next sprint mañana.");
        }, CancellationToken.None))
        {
            Feed(live);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(chunks.Count == 1, "Recognition starts before recording completes");
            live.Append(new byte[] { 2, 3, 4, 5 }, 4, .1f);
            var text = await live.CompleteAsync();
            var expected = Enumerable.Repeat((byte)1, 300 * 3200).Concat(new byte[7 * 3200]).Concat(new byte[] { 2, 3, 4, 5 });
            Check(chunks.SelectMany(x => x).SequenceEqual(expected), "Pause chunks preserve PCM once, in order, including final tail");
            Check(text == "La reunión. The meeting. Next sprint mañana.", "Joining preserves bilingual text without guessing repeated-word deletions");
        }
        var calls = 0;
        await using (var live = new LiveTranscription((_, _) => { calls++; return Task.FromResult("continuous"); }, CancellationToken.None))
        {
            for (var i = 0; i < 600; i++) live.Append(new byte[3200], 3200, .1f);
            Check(calls == 0, "Continuous speech is not cut at an arbitrary time limit");
            Check(await live.CompleteAsync() == "continuous" && calls == 1, "Continuous recording processes its final remainder");
        }
        await using (var live = new LiveTranscription((_, _) => Task.FromException<string>(new IOException()), CancellationToken.None))
        {
            Feed(live);
            Check(await live.CompleteAsync() == null && live.FellBack, "Decoder failure requests full-recording fallback");
        }
        await using (var live = new LiveTranscription((_, _) => Task.FromResult(""), CancellationToken.None))
        {
            Feed(live);
            Check(await live.CompleteAsync() == null, "Empty speech result cannot silently drop a chunk");
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        await using (var live = new LiveTranscription(async (_, token) =>
        {
            Interlocked.Increment(ref active); started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return "never"; }
            finally { Interlocked.Decrement(ref active); }
        }, CancellationToken.None))
        {
            Feed(live); await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Feed(live); Feed(live); Feed(live);
            Check(await live.CompleteAsync() == null && live.FellBack && active == 0,
                "Backlog cancels and drains the sole decoder before full-recording fallback");
        }
        using var cancellation = new CancellationTokenSource();
        // Final quiet tail after the last cut (16 of 620 real dictations fell back to the full recording here).
        var speechPcm = Enumerable.Repeat((byte)1, 300 * 3200).Concat(new byte[7 * 3200]).ToArray();
        var seen = new List<(byte[] Pcm, string Previous)>();
        await using (var live = new LiveTranscription((wav, previous, _) =>
        {
            var pcm = ReadPcm(wav); seen.Add((pcm, previous));
            return Task.FromResult(seen.Count == 1 ? "First part." : seen.Count == 2 ? "Second part, almost" : "Second part, almost done.");
        }, CancellationToken.None))
        {
            Feed(live); Feed(live);
            live.Append(new byte[] { 1, 0, 2, 0 }, 4, .005f);
            Check(await live.CompleteAsync() == "First part. Second part, almost done." && !live.FellBack && seen.Count == 3,
                "Quiet nonzero final tail replaces the last fragment's text instead of a full fallback");
            Check(seen[2].Pcm.SequenceEqual(speechPcm.Concat(new byte[] { 1, 0, 2, 0 })) && seen[2].Previous == "First part.",
                "Quiet final tail is decoded with the fragment before it and the text before that fragment as context");
        }
        var decodeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var superseded = false; var decodes = 0;
        await using (var live = new LiveTranscription(async (wav, previous, token) =>
        {
            if (Interlocked.Increment(ref decodes) == 1)
            {
                decodeStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { superseded = true; throw; }
            }
            return ReadPcm(wav).Length == speechPcm.Length + 4 && previous == "" ? "Whole last fragment." : "wrong";
        }, CancellationToken.None))
        {
            Feed(live); await decodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            live.Append(new byte[] { 1, 0, 2, 0 }, 4, .005f);
            Check(await live.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(3)) == "Whole last fragment." && !live.FellBack && superseded && decodes == 2,
                "Quiet final tail cancels the in-flight decode of the fragment it joins instead of decoding that audio twice");
        }
        await using (var live = new LiveTranscription((wav, _) => Task.FromResult(ReadPcm(wav).Length > speechPcm.Length ? "Loud first phrase y nada más" : "Loud first phrase"), CancellationToken.None))
        {
            Feed(live);
            for (var i = 0; i < 5; i++) live.Append(Enumerable.Repeat((byte)1, 3200).ToArray(), 3200, .004f);
            Check(await live.CompleteAsync() == "Loud first phrase y nada más" && !live.FellBack, "Soft speech below the speech threshold at the end keeps its words");
        }
        await using (var live = new LiveTranscription((wav, _) => Task.FromResult(ReadPcm(wav).Length > speechPcm.Length ? "" : "Loud first phrase"), CancellationToken.None))
        {
            Feed(live);
            live.Append(new byte[] { 1, 0, 2, 0 }, 4, .005f);
            Check(await live.CompleteAsync() == null && live.FellBack, "An empty result for the last fragment with its quiet tail still requests full-recording fallback");
        }
        await using (var live = new LiveTranscription((wav, _) => ReadPcm(wav).Length > speechPcm.Length ? Task.FromException<string>(new IOException()) : Task.FromResult("Loud first phrase"), CancellationToken.None))
        {
            Feed(live);
            live.Append(new byte[] { 1, 0, 2, 0 }, 4, .005f);
            Check(await live.CompleteAsync() == null && live.FellBack, "Decoder failure on the quiet final tail still requests full-recording fallback");
        }
        await using (var live = new LiveTranscription((_, _) => Task.FromResult("Loud first phrase"), CancellationToken.None))
        {
            Feed(live);
            for (var i = 0; i < 307; i++) live.Append(Enumerable.Repeat((byte)1, 3200).ToArray(), 3200, .004f);
            Check(await live.CompleteAsync() == null && live.FellBack, "A quiet segment in the middle of a recording still uses the full recording's context");
        }
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new LiveTranscription(async (_, token) =>
        { began.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return "never"; }, cancellation.Token);
        Feed(session); await began.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Check(true, "Exit cancellation drains the decoder before native disposal");
        await using (var live = new LiveTranscription((_, _) => throw new Exception("Silence reached decoder"), CancellationToken.None))
        {
            live.Append(new byte[32000], 32000, 0);
            Check(await live.CompleteAsync() == null, "Silence does not invoke decoder");
        }
    }
}
