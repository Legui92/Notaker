using System.IO;
using System.Threading.Channels;
using NAudio.Wave;

namespace Notaker;

/// <summary>One recording, one decoder. Optional work only: the caller retains the full WAV.</summary>
public sealed class LiveTranscription : IAsyncDisposable
{
    private const int BytesPerSecond = 32000;
    private readonly object gate = new();
    private readonly MemoryStream pending = new();
    private readonly Channel<(byte[] Pcm, bool QuietTail)> queue = Channel.CreateBounded<(byte[] Pcm, bool QuietTail)>(new BoundedChannelOptions(2)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource cancellation;
    private readonly Task<string?> worker;
    private bool finished;
    private volatile bool failed;
    private bool speech;
    private bool hasSignal;
    private int silentBytes;
    private int completedChunks;
    // Decode of the newest speech fragment; the quiet final tail cancels it, since that audio is decoded again anyway.
    private CancellationTokenSource? decoding;
    private readonly int minimumChunkBytes;
    public int CompletedChunks => Volatile.Read(ref completedChunks);
    public bool FellBack => failed;

    public LiveTranscription(Func<byte[], CancellationToken, Task<string>> recognize, CancellationToken token)
        : this((wav, _, t) => recognize(wav, t), token) { }

    /// <summary>
    /// The recognizer also receives the text already recognized in this recording (empty for the first fragment),
    /// so it can pass the end of it to Whisper as context. <paramref name="minimumChunkSeconds"/> exists for
    /// evaluation only; the app always uses the 30 s default.
    /// </summary>
    public LiveTranscription(Func<byte[], string, CancellationToken, Task<string>> recognize, CancellationToken token, int minimumChunkSeconds = 30)
    {
        minimumChunkBytes = Math.Max(1, minimumChunkSeconds) * BytesPerSecond;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        worker = Task.Run(async () =>
        {
            var parts = new List<string>();
            byte[]? last = null;
            var lastHasText = false;
            try
            {
                await foreach (var (chunk, quietTail) in queue.Reader.ReadAllAsync(cancellation.Token))
                {
                    if (failed) break;
                    // Alone, Whisper turns low noise into words (a stock phrase or the prompt). The quiet final tail is
                    // therefore decoded together with the fragment before it, which replaces that fragment's text:
                    // trailing noise becomes nothing, as in the full recording, and soft last words keep their context.
                    if (quietTail && last == null) throw new InvalidDataException("Quiet recording without a previous fragment.");
                    var pcm = quietTail ? [.. last!, .. chunk] : chunk;
                    if (quietTail && lastHasText) parts.RemoveAt(parts.Count - 1);
                    using var wav = new MemoryStream();
                    using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1)))
                    {
                        writer.Write(pcm, 0, pcm.Length);
                        writer.Flush();
                        using var item = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                        if (!quietTail) lock (gate) decoding = item;
                        string text;
                        try { text = await recognize(wav.ToArray(), string.Join(" ", parts), item.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (item.IsCancellationRequested && !cancellation.IsCancellationRequested)
                        {
                            // Superseded by the quiet final tail, which decodes this audio with it next.
                            last = pcm; lastHasText = false;
                            AppLog.Write("live.quiet_tail.superseded");
                            continue;
                        }
                        finally { lock (gate) decoding = null; }
                        // Never silently omit a chunk that contained speech.
                        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Empty live chunk.");
                        parts.Add(text.Trim());
                    }
                    last = pcm; lastHasText = true;
                    if (quietTail) AppLog.Write("live.quiet_tail.joined");
                    Interlocked.Increment(ref completedChunks);
                }
                return failed ? null : string.Join(" ", parts);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
            catch (Exception ex)
            {
                failed = true;
                AppLog.Write("live.failed.full_audio_retained", ex);
                return null;
            }
        });
    }

    // Called only after the recorder has persisted this buffer in the recovery recording.
    public void Append(byte[] pcm, int count, float level)
    {
        lock (gate)
        {
            if (finished || failed) return;
            pending.Write(pcm, 0, count);
            if (!hasSignal) hasSignal = pcm.AsSpan(0, count).IndexOfAnyExcept((byte)0) >= 0;
            if (level > 0.006f) { speech = true; silentBytes = 0; }
            else silentBytes += count;
            // Contiguous audio, no overlap or guessed text deduplication. Never force a cut mid-speech.
            if (pending.Length >= minimumChunkBytes && silentBytes >= 7 * BytesPerSecond / 10)
                Enqueue();
        }
    }

    private void Enqueue(bool final = false)
    {
        // RMS is a segmentation hint, not permission to delete quiet speech. A quiet segment in the middle of a
        // recording must be recognized in the full recording's context. The quiet final tail (after the last cut)
        // is recognized again with the fragment before it instead of the whole recording.
        var quietTail = final && !speech && hasSignal;
        // Only the fragment being decoded with nothing queued after it is the one the tail joins.
        var supersede = quietTail && queue.Reader.Count == 0 ? decoding : null;
        if ((!speech && hasSignal && !quietTail) || ((speech || quietTail) && !queue.Writer.TryWrite((pending.ToArray(), quietTail))))
        {
            failed = true;
            cancellation.Cancel();
            AppLog.Write(!speech && hasSignal && !quietTail ? "live.quiet_segment.full_audio_retained" : "live.backlog.full_audio_retained");
        }
        else supersede?.Cancel();
        pending.SetLength(0);
        speech = false;
        hasSignal = false;
        silentBytes = 0;
    }

    public async Task<string?> CompleteAsync()
    {
        lock (gate)
        {
            if (!finished)
            {
                if (!failed) Enqueue(final: true);
                finished = true;
                queue.Writer.TryComplete();
            }
        }
        var result = await worker.ConfigureAwait(false);
        return failed || string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) { finished = true; queue.Writer.TryComplete(); cancellation.Cancel(); }
        await worker.ConfigureAwait(false); // Do not dispose/reuse the native model before its decoder stops.
        pending.Dispose();
        cancellation.Dispose();
    }
}
