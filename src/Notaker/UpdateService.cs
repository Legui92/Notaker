using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Notaker;

public sealed record AvailableUpdate(Version Version, string AssetUrl, string Sha256, long Size);
public sealed record UpdatePlan(string Target, string Staged, string Sha256, int ParentPid, long ParentStartTicks, bool TestOnly = false, bool SimulateStartupFailure = false);

public sealed class UpdateService : IDisposable
{
    public const string Repository = "Legui92/Notaker";
    private const string ApiRoot = "https://api.github.com/repos/" + Repository;
    public static Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static string VersionLabel => CurrentVersion.ToString(3);
    private readonly HttpClient http;
    public UpdateService(HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        http.Timeout = TimeSpan.FromMinutes(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Notaker/" + VersionLabel);
    }
    public static async Task<string> GetTokenAsync(string encrypted, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(encrypted)) return SecretStore.Unprotect(encrypted);
        // Reuse the user's existing GitHub CLI session. Never log or persist this token.
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo("gh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.StartInfo.ArgumentList.Add("auth"); process.StartInfo.ArgumentList.Add("token");
            process.StartInfo.ArgumentList.Add("--hostname"); process.StartInfo.ArgumentList.Add("github.com");
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(token);
            var error = process.StandardError.ReadToEndAsync(token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(); throw; }
            await error;
            return process.ExitCode == 0 ? (await output).Trim() : "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return ""; }
    }
    private static HttpRequestMessage Request(string url, string key, bool binary = false)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || uri.Host != "api.github.com" || !uri.AbsolutePath.StartsWith("/repos/" + Repository + "/", StringComparison.Ordinal))
            throw new InvalidDataException("La URL de actualización no pertenece al repositorio de Notaker.");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/octet-stream" : "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
    public async Task<AvailableUpdate?> CheckAsync(string key, CancellationToken token, Version? installedVersion = null)
    {
        using var request = Request(ApiRoot + "/releases?per_page=30", key);
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            throw new InvalidOperationException("GitHub no permite consultar este repositorio privado. Inicia sesión con gh auth login o guarda un token con acceso de lectura a Contents de Legui92/Notaker. También puede haberse agotado el límite de GitHub.");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(token), installedVersion ?? CurrentVersion);
    }
    public static AvailableUpdate? ParseRelease(string json, Version current)
    {
        using var data = JsonDocument.Parse(json);
        var releases = data.RootElement.EnumerateArray()
            .Where(r => !r.GetProperty("draft").GetBoolean() && !r.GetProperty("prerelease").GetBoolean())
            .Select(r => (Release: r, Version: ParseVersion(r.GetProperty("tag_name").GetString())))
            .Where(r => r.Version != null && r.Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build)))
            .OrderByDescending(r => r.Version).ToList();
        if (releases.Count == 0) return null;
        var newest = releases[0];
        foreach (var asset in newest.Release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != "Notaker.exe") continue;
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            if (digest == null || !digest.StartsWith("sha256:") || !IsHash(digest[7..]))
                throw new InvalidDataException("La versión publicada no incluye una huella SHA-256 válida de GitHub.");
            var url = asset.GetProperty("url").GetString()!;
            using var validate = Request(url, "", true);
            var size = asset.GetProperty("size").GetInt64();
            if (size is < 1_000_000 or > 500_000_000) throw new InvalidDataException("Tamaño de actualización inesperado.");
            return new AvailableUpdate(newest.Version!, url, digest[7..], size);
        }
        throw new InvalidDataException("La última versión no contiene el ejecutable Notaker.exe.");
    }
    private static Version? ParseVersion(string? tag)
    {
        if (tag == null) return null;
        var value = tag.TrimStart('v', 'V');
        return Version.TryParse(value, out var version) && version.Build >= 0 && version.Revision < 0 ? version : null;
    }
    internal static bool IsHash(string hash) => hash.Length == 64 && hash.All(Uri.IsHexDigit);
    public async Task<string> DownloadAsync(AvailableUpdate update, string key, string directory, IProgress<double> progress, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Notaker.exe");
        using var request = Request(update.AssetUrl, key, true);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        try
        {
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                if (location == null || !location.IsAbsoluteUri || location.Scheme != "https" ||
                    !(location.Host == "release-assets.githubusercontent.com" || location.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Redirección de descarga no válida.");
                response.Dispose();
                // Signed asset URL: do not forward the GitHub credential to the CDN.
                response = await http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, token);
            }
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(path + ".partial"))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > update.Size) throw new InvalidDataException("La descarga supera el tamaño publicado.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress.Report(total * 100.0 / update.Size);
                }
                if (total != update.Size) throw new InvalidDataException("La descarga está incompleta.");
            }
            await VerifyHashAsync(path + ".partial", update.Sha256, token);
            var downloadedVersion = FileVersionInfo.GetVersionInfo(path + ".partial");
            if (new Version(downloadedVersion.FileMajorPart, downloadedVersion.FileMinorPart, downloadedVersion.FileBuildPart) != update.Version)
                throw new InvalidDataException("La versión del ejecutable no coincide con la publicación.");
            File.Move(path + ".partial", path, true);
            return path;
        }
        finally { response.Dispose(); if (File.Exists(path + ".partial")) File.Delete(path + ".partial"); }
    }
    public static async Task VerifyHashAsync(string path, string expected, CancellationToken token)
    {
        if (!IsHash(expected)) throw new InvalidDataException("Huella SHA-256 no válida.");
        await using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La verificación de integridad falló. No se instalará la actualización.");
    }
    public static void LaunchInstaller(string staged, string hash)
    {
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("No se encuentra el ejecutable actual.");
        if (!Path.GetFileName(target).Equals("Notaker.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Usa el ejecutable publicado Notaker.exe para actualizar.");
        // Probe write access while the current application is still running.
        var probe = Path.Combine(Path.GetDirectoryName(target)!, ".notaker-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        var directory = Path.GetDirectoryName(staged)!;
        var helper = Path.Combine(directory, "Notaker.Update.exe");
        File.Copy(target, helper, true);
        using var process = Process.GetCurrentProcess();
        var plan = new UpdatePlan(target, staged, hash, process.Id, process.StartTime.ToUniversalTime().Ticks);
        var planFile = Path.Combine(directory, "update.json");
        File.WriteAllText(planFile, JsonSerializer.Serialize(plan));
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(planFile);
        using var helperProcess = Process.Start(start) ?? throw new IOException("No se pudo iniciar el instalador de la actualización.");
    }
    public void Dispose() => http.Dispose();
}
