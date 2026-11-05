using System.IO;
using NAudio.Wave;
using Notaker;

internal static class NativeCancellationChecks
{
    public static async Task Run(string model, string fixture)
    {
        using var reader = new WaveFileReader(fixture);
        var pcm = new byte[reader.Length]; reader.ReadExactly(pcm);
        using var buffer = new MemoryStream();
        using var writer = new WaveFileWriter(buffer,reader.WaveFormat);
        while (writer.TotalTime.TotalSeconds < 120) writer.Write(pcm);
        writer.Flush();
        var audio = buffer.ToArray();
        await VerifyNativeDisposalContract(model,audio);
        var engine = new Transcriber();
        await engine.PrepareAsync(model,true,CancellationToken.None);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await engine.PrepareAsync(model,true,CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            var task = engine.TranscribeAsync(model,audio,"es",cancellation.Token);
            await Task.Delay(150);
            if (task.IsCompleted) throw new Exception("Fixture finished before native cancellation could be tested");
            cancellation.Cancel();
            try { await task.WaitAsync(TimeSpan.FromSeconds(15)); throw new Exception("Native cancellation did not propagate"); }
            catch (OperationCanceledException) { }
            Console.WriteLine($"PASS: active native cancellation {attempt+1} preserves OperationCanceledException without synchronous-disposal error");
            var next = await engine.TranscribeAsync(model,await File.ReadAllBytesAsync(fixture),"es",CancellationToken.None,useGpu:attempt%2==0);
            if (!next.Contains("mañana",StringComparison.OrdinalIgnoreCase)) throw new Exception("Recognition did not recover after native cancellation");
        }
        try { await engine.TranscribeAsync(model,[],"es",CancellationToken.None); throw new Exception("Invalid WAV was accepted"); }
        catch (Exception ex) when (ex is not OperationCanceledException && ex.Message != "Invalid WAV was accepted")
        { Console.WriteLine("PASS: invalid audio without cancellation retains a real failure rather than becoming cancellation"); }
        engine.Dispose();
        Console.WriteLine("PASS: subsequent recognition and model reload succeed after every active native cancellation");
    }
    private static async Task VerifyNativeDisposalContract(string model, byte[] audio)
    {
        using var factory = Whisper.net.WhisperFactory.FromPath(model);
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int segmentSeen = 0;
        var processor = factory.CreateBuilder().WithLanguage("es")
            .WithSegmentEventHandler(_ => Volatile.Write(ref segmentSeen,1))
            .WithProgressHandler(_ => { if (Volatile.Read(ref segmentSeen) == 1) { blocked.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); } }).Build();
        await using var stream = new MemoryStream(audio);
        await using var segments = processor.ProcessAsync(stream,cancellation.Token).GetAsyncEnumerator();
        try
        {
            if (!await segments.MoveNextAsync()) throw new Exception("No native segment available");
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(8));
            cancellation.Cancel();
            try { while (await segments.MoveNextAsync()) { } throw new Exception("Expected iterator cancellation"); }
            catch (OperationCanceledException) { }
            try { processor.Dispose(); throw new Exception("Native worker was not held while disposing"); }
            catch (Exception ex) when (ex.Message == "Cannot dispose while processing, please use DisposeAsync instead.")
            { Console.WriteLine("PASS: reproduced exact reported synchronous-disposal error with native worker still processing"); }
        }
        finally { cancellation.Cancel(); release.Set(); await processor.DisposeAsync(); }
        Console.WriteLine("PASS: asynchronous disposal waits for native worker before releasing its state");
    }
}
