using System.IO;
using NAudio.Wave;

namespace Notaker;

public sealed class Recorder : IDisposable
{
    private readonly WaveInEvent input;
    private readonly MemoryStream stream = new();
    private readonly WaveFileWriter writer;
    private readonly TaskCompletionSource<byte[]> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool stopping;
    private int voicedBuffers;
    private int bufferCount;
    public float Level { get; private set; }
    public bool HasSpeech => voicedBuffers >= 3;
    public event Action<Exception>? Failed;

    public Recorder(int device)
    {
        input = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        writer = new WaveFileWriter(stream, input.WaveFormat);
        input.DataAvailable += (_, e) =>
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
            double squares = 0;
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i) / 32768.0;
                squares += sample * sample;
            }
            Level = (float)Math.Sqrt(squares / Math.Max(1, e.BytesRecorded / 2));
            if (Level > 0.006) Interlocked.Increment(ref voicedBuffers);
            // Bound memory even if the UI thread is temporarily blocked (10 minutes).
            if (Interlocked.Increment(ref bufferCount) >= 6000) input.StopRecording();
        };
        input.RecordingStopped += (_, e) =>
        {
            writer.Flush();
            if (e.Exception != null) { stopped.TrySetException(e.Exception); Failed?.Invoke(e.Exception); }
            else stopped.TrySetResult(stream.ToArray());
        };
    }
    public void Start() => input.StartRecording();
    public async Task<byte[]> StopAsync()
    {
        if (!stopping) { stopping = true; input.StopRecording(); }
        return await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    public void Dispose() { input.Dispose(); writer.Dispose(); stream.Dispose(); }
}
