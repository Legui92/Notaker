using Notaker;
using System.IO;

var root = Path.Combine(Path.GetTempPath(), "Notaker-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception("FAIL: " + message);
    checks++; Console.WriteLine("PASS: " + message);
}
try
{
    if (args.Length == 5 && args[0] == "--benchmark")
    {
        Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = args[1] == "cpu"
            ? [Whisper.net.LibraryLoader.RuntimeLibrary.Cpu]
            : [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan];
        using var logger = Whisper.net.Logger.LogProvider.AddConsoleLogging(Whisper.net.Logger.WhisperLogLevel.Info);
        using var benchmarkEngine = new Transcriber();
        var wav = await File.ReadAllBytesAsync(args[3]);
        for (var run = 0; run < 3; run++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await benchmarkEngine.TranscribeAsync(args[2], wav, args[4], CancellationToken.None);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { run, backend = Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary.ToString(), seconds = watch.Elapsed.TotalSeconds, text = result }));
        }
        return 0;
    }
    var storage = new Storage(root);
    storage.Settings.Language = "es";
    storage.Settings.Hotkey = 2;
    storage.SaveSettings();
    storage.Settings.ProtectedApiKey = SecretStore.Protect("migration-test-ai-key");
    storage.Settings.ProtectedGitHubToken = "obsolete-unreadable-credential";
    storage.SaveSettings();
    var migrated = new Storage(root);
    Check(migrated.Settings.ProtectedGitHubToken == null && !File.ReadAllText(Path.Combine(root, "settings.json")).Contains("ProtectedGitHubToken"), "Legacy GitHub token is removed without decrypting it");
    Check(SecretStore.Unprotect(migrated.Settings.ProtectedApiKey) == "migration-test-ai-key" && migrated.Settings.Language == "es", "Removing GitHub credentials preserves the AI key and preferences");
    var reloaded = new Storage(root);
    Check(reloaded.Settings.Language == "es" && reloaded.Settings.Hotkey == 2, "Language and hotkey persist across restarts");
    Check(reloaded.Settings.UseGpu, "Existing settings enable GPU acceleration by default");
    reloaded.Settings.UseGpu = false; reloaded.SaveSettings();
    Check(!new Storage(root).Settings.UseGpu, "CPU preference persists across restarts");
    var sample = new Dictation("Español: reunión mañana. English: let's go!", DateTime.Now, 3) { RecognitionSeconds = 1.2, PolishSeconds = 0.8 };
    storage.Add(sample);
    Check(new Storage(root).History[0].Text == sample.Text, "Unicode dictation survives storage round trip");
    Check(new Storage(root).History[0].RecognitionSeconds == 1.2 && new Storage(root).History[0].PolishSeconds == 0.8, "Voice and AI timings survive history round trip");
    storage.Settings.KeepHistory = false;
    storage.Add(sample with { Text = "Do not save" });
    Check(new Storage(root).History.Count == 1, "History opt-out prevents disk persistence");
    storage.Settings.KeepHistory = true;
    for (int i = 0; i < 205; i++) storage.Add(sample with { Text = i.ToString() });
    reloaded = new Storage(root);
    Check(reloaded.History.Count == 200 && reloaded.History[0].Text == "204" && reloaded.History[^1].Text == "5", "History retains newest 200 entries");
    storage.ClearHistory();
    Check(new Storage(root).History.Count == 0, "Clearing history persists");
    File.WriteAllText(Path.Combine(root, "settings.json"), "{broken");
    reloaded = new Storage(root);
    Check(reloaded.Settings.Language == "auto" && reloaded.LoadWarning != null && Directory.GetFiles(root, "*.corrupt-*").Length == 1, "Corrupt settings recover with backup");
    File.WriteAllText(Path.Combine(root, "settings.json"), "{\"Language\":\"invalid\",\"Hotkey\":99}");
    reloaded = new Storage(root);
    Check(reloaded.Settings.Language == "auto" && reloaded.Settings.Hotkey == 2, "Invalid preferences cannot crash hotkey setup");
    Check(reloaded.Settings.Shortcut == new DictationShortcut(9, 0x44), "Legacy dropdown selection migrates without changing the shortcut");
    var custom = DictationShortcut.Capture(System.Windows.Input.Key.K, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift);
    Check(custom == new DictationShortcut(6, 0x4B) && custom.DisplayName == "Ctrl + Shift + K", "Keyboard capture converts custom combination to Windows registration values");
    reloaded.Settings.CustomHotkey = custom; reloaded.SaveSettings();
    Check(new Storage(root).Settings.Shortcut == custom, "Custom shortcut persists across restarts");
    Check(DictationShortcut.Capture(System.Windows.Input.Key.LeftCtrl, System.Windows.Input.ModifierKeys.Control) == null, "Modifier alone does not replace the captured shortcut");
    Check(DictationShortcut.Capture(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.None) == null, "Normal typing cannot become an unmodified letter shortcut");
    Check(DictationShortcut.Capture(System.Windows.Input.Key.F9, System.Windows.Input.ModifierKeys.None)?.IsValid == true, "Standalone function key can be captured");
    Check(!new DictationShortcut(3, 0x2E).IsValid && !new DictationShortcut(2, 0x7B).IsValid, "Reserved Ctrl+Alt+Delete and F12 combinations are rejected");
    reloaded.Settings.CustomHotkey = new DictationShortcut(0xFFFF, 0); reloaded.SaveSettings();
    Check(new Storage(root).Settings.Shortcut == DictationShortcut.FromLegacy(2), "Invalid stored shortcut falls back safely");
    foreach (var model in VoiceModels.All)
    {
        storage.Settings.Model = model.Id;
        storage.Settings.Language = "mixed";
        storage.Settings.ProtectedApiKey = SecretStore.Protect("upgrade-test-key");
        storage.SaveSettings();
        var upgraded = new Storage(root);
        Check(upgraded.Settings.Model == model.Id && upgraded.Settings.Language == "mixed"
            && SecretStore.Unprotect(upgraded.Settings.ProtectedApiKey) == "upgrade-test-key", "Model, Spanglish and protected key persist: " + model.Id);
    }
    storage.Settings.Model = "../unsupported"; storage.SaveSettings();
    Check(new Storage(root).Settings.Model == "base", "Unsupported model cannot escape model directory");
    Check(DictationText.CleanArtifacts("Esta⠈versión⠂incluye el calendario⡀ de dividendos.") == "Esta versión incluye el calendario de dividendos.", "Braille artifacts become word boundaries");
    Check(DictationText.CleanArtifacts("Revisar el pull request\nMañana: deployment ✅ + C# / API.") == "Revisar el pull request\nMañana: deployment ✅ + C# / API.", "Spanglish, newlines, accents and meaningful symbols survive cleaning");
    Check(DictationText.CleanArtifacts("API\u200B Key\uFEFF\u0000") == "API Key", "Invisible artifacts are removed");
    var polishRequests = 0;
    using (var mixedPolisher = new TextPolisher(new StubHttp((_, body) =>
    {
        polishRequests++;
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var data = json.RootElement;
        var prompt = data.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Check(prompt.Contains("Spanglish/code-switching") && prompt.Contains("Do not translate") && prompt.Contains("do not guess"), "Editor preserves mixed language and avoids guessing ambiguous words");
        Check(data.GetProperty("model").GetString() == "deepseek-flash" && data.GetProperty("thinking").GetProperty("type").GetString() == "disabled", "Cleanup keeps Flash without reasoning cost");
        using var input = System.Text.Json.JsonDocument.Parse(data.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Check(input.RootElement.GetProperty("dictated_text").GetString() == "Revisa el pull request", "Artifacts are removed before sending text to AI");
        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Revisa el⡀ pull request.\"}}]}") };
    })))
    {
        Check(await mixedPolisher.PolishAsync("Revisa el⡀ pull request", "mock", "deepseek-flash", [], CancellationToken.None) == "Revisa el pull request." && polishRequests == 1, "One API call per cleanup; output artifacts are removed");
    }
    using (var shrinkingPolisher = new TextPolisher(new StubHttp((_, _) => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Resumen.\"}}]}") })))
    {
        try { await shrinkingPolisher.PolishAsync(new string('a', 200), "mock", "deepseek-flash", [], CancellationToken.None); throw new Exception("Destructive summary accepted"); }
        catch (InvalidOperationException) { Check(true, "Overly destructive editing is rejected"); }
    }
    Check(!Transcriber.ModelExists(Path.Combine(root, "absent.bin")), "Missing model is detected");
    File.WriteAllBytes(Path.Combine(root, "partial.bin"), [1, 2, 3]);
    Check(!Transcriber.ModelExists(Path.Combine(root, "partial.bin")), "Partial download is not treated as installed model");
    Check(PersonalVocabulary.Normalize([" Acme Labs ", "acme labs", "", "X", "Notaker"]).SequenceEqual(["Acme Labs", "Notaker"]), "Vocabulary deduplicates phrases and rejects empty entries");
    Check(PersonalVocabulary.Learn("enviar a Luisa mañana", "enviar a Luzia mañana").SequenceEqual(["Luzia"]), "Corrections teach newly introduced name spellings");
    Check(!PersonalVocabulary.Learn("Enviar el texto", "Enviar el texto.").Any(), "Punctuation corrections do not pollute vocabulary");
    Check(PersonalVocabulary.Prompt(Enumerable.Range(0, 100).Select(i => "Terminología" + i)).Length <= 600, "Whisper vocabulary prompt stays bounded");
    storage.SaveVocabulary(["Notaker"]);
    storage.Add(sample);
    var learned = storage.Correct(sample, "Reunión con Luzia en Notaker.");
    reloaded = new Storage(root);
    Check(learned == 1 && reloaded.Vocabulary.Contains("Luzia") && reloaded.History[0].OriginalText == sample.Text, "Corrections preserve original text and persist learned words");
    storage.Settings.LearnVocabulary = false;
    storage.Correct(storage.History[0], "Reunión con QwertyCorp.");
    Check(!new Storage(root).Vocabulary.Contains("QwertyCorp"), "Learning opt-out prevents vocabulary collection");
    var protectedKey = SecretStore.Protect("test-key-never-used-on-network");
    Check(!protectedKey.Contains("test-key") && SecretStore.Unprotect(protectedKey) == "test-key-never-used-on-network", "Windows protects API key and can recover it");
    using (var polisher = new TextPolisher(new StubHttp((request, body) =>
    {
        using var json = System.Text.Json.JsonDocument.Parse(body);
        Check(request.RequestUri?.Host == "api.deepseek.com" && request.Headers.Authorization?.Scheme == "Bearer", "Polisher uses fixed HTTPS provider with authorization header");
        Check(json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("dictated_text"), "Dictation and vocabulary are serialized as data");
        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Enviar el informe mañana.\"}}]}") };
    })))
    {
        Check(await polisher.PolishAsync("Eh, enviar el informe mañana.", "mock-key", "deepseek-flash", ["Notaker"], CancellationToken.None) == "Enviar el informe mañana.", "Polisher returns completed edited text (mock provider)");
    }
    foreach (var reason in new[] { "length", "content_filter" })
    {
        using var polisher = new TextPolisher(new StubHttp((_, _) => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent($"{{\"choices\":[{{\"finish_reason\":\"{reason}\",\"message\":{{\"content\":\"Partial\"}}}}]}}") }));
        try { await polisher.PolishAsync("Texto original", "mock", "model", [], CancellationToken.None); throw new Exception("Partial result accepted"); }
        catch (InvalidOperationException) { Check(true, "Rejects incomplete AI output: " + reason); }
    }
    using (var polisher = new TextPolisher(new StubHttp((_, _) => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized))))
    {
        try { await polisher.PolishAsync("Texto", "mock", "model", [], CancellationToken.None); throw new Exception("Unauthorized response accepted"); }
        catch (System.Net.Http.HttpRequestException ex) { Check(ex.Message.Contains("clave"), "API authentication failure is actionable without exposing secrets"); }
    }
    if (args.Length == 3)
    {
        using var engine = new Transcriber();
        var english = await engine.TranscribeAsync(args[0], await File.ReadAllBytesAsync(args[1]), "en", CancellationToken.None);
        Console.WriteLine("EN: " + english);
        Check(english.Contains("country", StringComparison.OrdinalIgnoreCase) && english.Contains("fellow", StringComparison.OrdinalIgnoreCase), "Native English transcription recognizes public JFK fixture");
        var spanish = await engine.TranscribeAsync(args[0], await File.ReadAllBytesAsync(args[2]), "es", CancellationToken.None);
        Console.WriteLine("ES: " + spanish);
        Check(spanish.Contains("reunión", StringComparison.OrdinalIgnoreCase) && spanish.Contains("mañana", StringComparison.OrdinalIgnoreCase), "Native Spanish transcription recognizes synthesized fixture");
        var auto = await engine.TranscribeAsync(args[0], await File.ReadAllBytesAsync(args[2]), "auto", CancellationToken.None);
        Check(auto.Contains("mañana", StringComparison.OrdinalIgnoreCase), "Automatic language detection preserves Spanish instead of translating");
        var cpu = await engine.TranscribeAsync(args[0], await File.ReadAllBytesAsync(args[2]), "es", CancellationToken.None, useGpu: false);
        Check(cpu.Contains("mañana", StringComparison.OrdinalIgnoreCase), "Disabling GPU reloads the model for CPU recognition");
        var gpuAgain = await engine.TranscribeAsync(args[0], await File.ReadAllBytesAsync(args[2]), "es", CancellationToken.None);
        Check(gpuAgain.Contains("mañana", StringComparison.OrdinalIgnoreCase), "GPU can be re-enabled after CPU recognition");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await engine.TranscribeAsync(args[0], [], "es", cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Check(true, "Cancelled transcription does not start inference"); }
    }
    await UpdateChecks.RunAsync();
    if (args.Length == 2 && args[0] == "--live-update")
    {
        using var updater = new UpdateService();
        var release = await updater.CheckAsync(CancellationToken.None, new Version(0, 0, 0)) ?? throw new Exception("No release published");
        var downloaded = await updater.DownloadAsync(release, args[1], new Progress<double>(), CancellationToken.None);
        Check(File.Exists(downloaded), "Live public GitHub release downloads with verified hash and version");
    }
    if (args.Contains("--desktop") || args.Contains("--desktop-fail-focus"))
        await DesktopChecks.RunAsync(args.Contains("--desktop-fail-focus"));
    Console.WriteLine($"{checks} checks passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    return 1;
}
finally
{
    // Delete only the uniquely named test directory created by this process.
    try
    {
        if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(root).StartsWith("Notaker-tests-", StringComparison.Ordinal)) Directory.Delete(root, true);
    }
    catch (Exception ex) { Console.Error.WriteLine("Could not remove test data: " + ex.Message); }
}

sealed class StubHttp(Func<System.Net.Http.HttpRequestMessage, string, System.Net.Http.HttpResponseMessage> respond) : System.Net.Http.HttpMessageHandler
{
    protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        => respond(request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
}
