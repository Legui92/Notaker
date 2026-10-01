using System.IO;
using Notaker;
using System.Net;
using System.Net.Http;
using System.Reflection;

internal static class PolishTimeoutChecks
{
    public static async Task Run()
    {
        using var polisher = new TextPolisher(new StalledHandler());
        var client = (HttpClient)typeof(TextPolisher).GetField("http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(polisher)!;
        client.Timeout = TimeSpan.FromMilliseconds(150);
        using var cleanup = new CancellationTokenSource();
        var task = polisher.PolishAsync("Texto de prueba", "synthetic", "mock", [], cleanup.Token);
        try
        {
            var winner = await Task.WhenAny(task, Task.Delay(1200));
            if (winner != task) throw new Exception("FAIL: AI response body ignored the request deadline and left polishing pending");
            try { await task; throw new Exception("FAIL: stalled body succeeded"); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
            Console.WriteLine("PASS: stalled AI response body respects the total deadline");
        }
        finally { cleanup.Cancel(); try { await task; } catch { } }
    }
    internal sealed class StalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
    }
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(Timeout.Infinite, token); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
