using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Notaker;

internal static class UpdateInstaller
{
    internal static async Task ApplyAsync(string planPath)
    {
        planPath = Path.GetFullPath(planPath);
        var directory = Path.GetDirectoryName(planPath)!;
        var plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(planPath)) ?? throw new InvalidDataException("Plan de actualización vacío.");
        var target = Path.GetFullPath(plan.Target);
        var staged = Path.GetFullPath(plan.Staged);
        if (!Path.GetFileName(target).Equals("Notaker.exe", StringComparison.OrdinalIgnoreCase)
            || !Path.GetDirectoryName(staged)!.Equals(directory, StringComparison.OrdinalIgnoreCase)
            || target.Equals(staged, StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
            throw new InvalidDataException("Rutas de actualización no válidas.");
        await UpdateService.VerifyHashAsync(staged, plan.Sha256, CancellationToken.None);
        if (FileVersionInfo.GetVersionInfo(staged).FileMajorPart == 0 && FileVersionInfo.GetVersionInfo(staged).FileMinorPart == 0)
            throw new InvalidDataException("El archivo descargado no es un ejecutable válido de Notaker.");
        // Wait for exactly the initiating process, not an unrelated reused process ID.
        try
        {
            using var parent = Process.GetProcessById(plan.ParentPid);
            if (!parent.HasExited)
            {
                DateTime started;
                try { started = parent.StartTime; }
                catch (InvalidOperationException) when (parent.HasExited) { started = default; }
                if (started != default)
                {
                    if (started.ToUniversalTime().Ticks != plan.ParentStartTicks) throw new InvalidOperationException("El proceso de origen cambió.");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    await parent.WaitForExitAsync(timeout.Token);
                }
            }
        }
        catch (ArgumentException) { /* The initiating process already exited. */ }
        var replacement = Path.Combine(Path.GetDirectoryName(target)!, ".notaker-" + Guid.NewGuid().ToString("N") + ".new");
        var backup = target + ".previous";
        var confirmation = Path.Combine(directory, "started.ok");
        var installed = false;
        Process? launched = null;
        try
        {
            File.Copy(staged, replacement);
            await UpdateService.VerifyHashAsync(replacement, plan.Sha256, CancellationToken.None);
            for (var attempt = 0; ; attempt++)
            {
                try { File.Replace(replacement, target, backup, true); installed = true; break; }
                catch (IOException) when (attempt < 20) { await Task.Delay(500); }
            }
            if (File.Exists(confirmation)) File.Delete(confirmation);
            var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
            start.ArgumentList.Add("--update-confirm"); start.ArgumentList.Add(confirmation);
            if (plan.TestOnly) start.ArgumentList.Add(plan.SimulateStartupFailure ? "--update-test-fail" : "--update-test");
            launched = Process.Start(start) ?? throw new IOException("No se pudo reiniciar Notaker.");
            for (var i = 0; i < 120 && !File.Exists(confirmation); i++)
            {
                if (launched.HasExited) break;
                await Task.Delay(250);
            }
            if (!File.Exists(confirmation)) throw new IOException("La nueva versión no confirmó el inicio. Se restaurará la anterior.");
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), "{\"success\":true}");
            // Keep one previous executable for recovery. User data is never touched.
            File.Delete(staged);
        }
        catch (Exception ex)
        {
            if (installed)
            {
                if (launched is { HasExited: false }) { launched.Kill(); await launched.WaitForExitAsync(); }
                if (File.Exists(backup)) File.Move(backup, target, true);
                if (!plan.TestOnly) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { success = false, error = ex.Message }));
            throw;
        }
        finally { launched?.Dispose(); if (File.Exists(replacement)) File.Delete(replacement); }
    }
}
