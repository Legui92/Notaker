using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Notaker;

internal static class UpdateChecks
{
    internal static async Task RunAsync()
    {
        var digest = new string('a', 64);
        var asset = new { name = "Notaker.exe", url = "https://api.github.com/repos/Legui92/Notaker/releases/assets/123", digest = "sha256:" + digest, size = 73_000_000 };
        string Releases(string version, bool draft = false, bool prerelease = false) => JsonSerializer.Serialize(new[] { new { tag_name = version, draft, prerelease, assets = new[] { asset } } });
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        Check(UpdateService.ParseRelease(Releases("v0.4.0"), new Version(0, 3, 0, 0))?.Version == new Version(0, 4, 0), "Updater finds a newer stable release");
        Check(UpdateService.ParseRelease(Releases("v0.3.0"), new Version(0, 3, 0, 0)) == null, "Updater does not reinstall current three-part version");
        Check(UpdateService.ParseRelease(Releases("v0.2.0"), new Version(0, 3, 0)) == null, "Updater rejects downgrade");
        Check(UpdateService.ParseRelease(Releases("v0.4.0", draft: true), new Version(0, 3, 0)) == null && UpdateService.ParseRelease(Releases("v0.4.0", prerelease: true), new Version(0, 3, 0)) == null, "Updater ignores draft and prerelease builds");
        foreach (var invalid in new[] { Releases("v0.4.0").Replace(digest, "bad"), Releases("v0.4.0").Replace("api.github.com", "attacker.example") })
        {
            try { UpdateService.ParseRelease(invalid, new Version(0, 3, 0)); throw new Exception("Invalid release accepted"); }
            catch (InvalidDataException) { Check(true, "Updater rejects untrusted asset metadata"); }
        }
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "downloaded payload");
            var correct = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)));
            await UpdateService.VerifyHashAsync(file, correct, CancellationToken.None);
            Check(true, "Updater accepts matching SHA-256");
            try { await UpdateService.VerifyHashAsync(file, digest, CancellationToken.None); throw new Exception("Hash mismatch accepted"); }
            catch (InvalidDataException) { Check(true, "Updater rejects corrupted download before installation"); }
        }
        finally { File.Delete(file); }
        using var service = new UpdateService(new StubHttp((request, _) =>
        {
            Check(request.Headers.Authorization?.Parameter == "test-github-token", "Private repository request uses authentication");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        try { await service.CheckAsync("test-github-token", CancellationToken.None); throw new Exception("Private repo error ignored"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("privado"), "Private repository access failure is actionable"); }
    }
}
