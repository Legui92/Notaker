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
    private readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource cancellation;
    private readonly Task<string?> worker;
    private bool finished;
    private volatile bool failed;
    private bool speech;
    private bool hasSignal;
    private int silentBytes;
    private int completedChunks;
    public int CompletedChunks => Volatile.Read(ref completedChunks);
    public bool FellBack => failed;

    public LiveTranscription(Func<byte[], CancellationToken, Task<string>> recognize, CancellationToken token)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        worker = Task.Run(async () =>
        {
            var parts = new List<string>();
            try
            {
                await foreach (var pcm in queue.Reader.ReadAllAsync(cancellation.Token))
                {
                    if (failed) break;
                    using var wav = new MemoryStream();
                    using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1)))
                    {
                        writer.Write(pcm, 0, pcm.Length);
                        writer.Flush();
                        var text = await recognize(wav.ToArray(), cancellation.Token).ConfigureAwait(false);
                        // Never silently omit a chunk that contained speech.
                        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Empty live chunk.");
                        parts.Add(text.Trim());
                    }
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
            if (pending.Length >= 30 * BytesPerSecond && silentBytes >= 7 * BytesPerSecond / 10)
                Enqueue();
        }
    }

    private void Enqueue()
    {
        // RMS is a segmentation hint, not permission to delete quiet speech.
        // An ambiguous quiet segment must be recognized in the full recording's context.
        if ((!speech && hasSignal) || (speech && !queue.Writer.TryWrite(pending.ToArray())))
        {
            failed = true;
            cancellation.Cancel();
            AppLog.Write(!speech && hasSignal ? "live.quiet_segment.full_audio_retained" : "live.backlog.full_audio_retained");
        }
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
                if (!failed) Enqueue();
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
