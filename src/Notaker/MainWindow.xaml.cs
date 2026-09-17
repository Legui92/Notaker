using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.Wave;
using Forms = System.Windows.Forms;

namespace Notaker;

public partial class MainWindow : Window
{
    private readonly Storage storage;
    private readonly Transcriber transcriber = new();
    private readonly TextPolisher polisher = new();
    private readonly DictationOverlay overlay = new();
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch duration = new();
    private readonly CancellationTokenSource lifetime = new();
    private Recorder? recorder;
    private IntPtr handle, target;
    private HwndSource? source;
    private int hotkeyId = 1;
    private bool ready, busy, exiting, registered;
    private bool capturingShortcut;
    private DictationShortcut? pendingShortcut;
    private Task? operation;
    private string? lastText;
    private Dictation? lastDictation;
    private long overlayVersion;

    public MainWindow(string? dataRoot = null)
    {
        storage = new Storage(dataRoot);
        InitializeComponent();
        AppVersionLabel.Text = "NOTAKER / WINDOWS · " + UpdateService.VersionLabel;
        LanguageBox.SelectedIndex = storage.Settings.Language == "es" ? 1 : storage.Settings.Language == "en" ? 2 : 0;
        ModelBox.SelectedIndex = storage.Settings.Model == "small" ? 1 : 0;
        HotkeyBox.Text = storage.Settings.Shortcut.DisplayName;
        MicrophoneBox.Items.Add("Predeterminado del sistema");
        for (int i = 0; i < WaveIn.DeviceCount; i++) MicrophoneBox.Items.Add(WaveIn.GetCapabilities(i).ProductName);
        MicrophoneBox.SelectedIndex = Math.Clamp(storage.Settings.Microphone + 1, 0, MicrophoneBox.Items.Count - 1);
        storage.Settings.Microphone = MicrophoneBox.SelectedIndex - 1;
        PasteBox.IsChecked = storage.Settings.AutoPaste;
        HistoryBox.IsChecked = storage.Settings.KeepHistory;
        tray = new Forms.NotifyIcon { Text = "Notaker · Dictado", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir Notaker", null, (_, _) => ShowMain());
        menu.Items.Add("Copiar último dictado", null, async (_, _) => { if (lastText != null) await Native.CopyAsync(lastText); });
        menu.Items.Add("Corregir último dictado", null, (_, _) => { if (lastDictation != null) Correct(lastDictation); });
        menu.Items.Add("Salir", null, async (_, _) => await ExitAsync());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowMain();
        timer.Tick += (_, _) =>
        {
            if (recorder == null) return;
            overlay.Update($"Escuchando · {duration.Elapsed:mm\\:ss}", recorder.Level);
            LiveText.Text = $"Grabando {duration.Elapsed:mm\\:ss} · Repite el atajo para terminar";
            if (duration.Elapsed >= TimeSpan.FromMinutes(10) && !busy) StartToggle();
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            source = HwndSource.FromHwnd(handle); source.AddHook(WindowMessage);
            registered = Native.Register(handle, storage.Settings.Shortcut, hotkeyId);
            ready = true; RefreshStatus();
            if (!registered) SetStatus("Atajo ocupado", "Selecciona otra combinación: otra aplicación está usando este atajo.");
            else if (storage.LoadWarning != null) SetStatus("Datos recuperados", storage.LoadWarning);
        };
        Closing += OnClosing;
        Deactivated += (_, _) => { if (capturingShortcut) Keyboard.ClearFocus(); };
        RefreshHistory();
    }
    private void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void SetStatus(string heading, string detail) { StatusHeading.Text = heading; StatusText.Text = detail; }
    private void RefreshStatus()
    {
        var installed = Transcriber.ModelExists(storage.ModelPath);
        PrivacyLabel.Text = storage.Settings.CleanWithAi ? "Audio en tu equipo.\nTexto a DeepSeek." : "Tu audio se queda\nen este equipo.";
        ShortcutLabel.Text = storage.Settings.Shortcut.DisplayName;
        SetupButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        var modelDescription = storage.Settings.Model == "small" ? "Whisper small (aprox. 466 MiB)" : "Whisper base (aprox. 142 MiB)";
        SetStatus(installed ? "Todo listo. Dale voz a tus ideas." : "Prepara tu primer dictado", installed ? "Sitúa el cursor en otra aplicación y pulsa el atajo para hablar." : $"Descarga {modelDescription}. Después funciona sin internet.");
        LiveText.Text = "";
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == hotkeyId) { handled = true; StartToggle(); }
        return IntPtr.Zero;
    }
    private void StartToggle()
    {
        if (busy || exiting || capturingShortcut || OwnedWindows.OfType<Window>().Any(w => w.IsVisible)) return;
        operation = ToggleAsync();
    }
    private async Task ToggleAsync()
    {
        try
        {
            if (recorder == null)
            {
                if (!Transcriber.ModelExists(storage.ModelPath)) { ShowMain(); SetStatus("Falta el modelo de voz", "Pulsa Descargar modelo para preparar el dictado local."); return; }
                if (WaveIn.DeviceCount == 0) throw new InvalidOperationException("No se encontró un micrófono. Conecta uno y vuelve a abrir Notaker.");
                target = Native.GetForegroundWindow();
                if (target == handle) { SetStatus("Elige dónde escribir", "Sitúa el cursor en el editor, chat o documento de destino y pulsa el atajo."); return; }
                recorder = new Recorder(storage.Settings.Microphone);
                recorder.Failed += ex => Dispatcher.BeginInvoke(new Action(() => RecordingFailed(ex)));
                recorder.Start();
                duration.Restart(); SettingsPanel.IsEnabled = false;
                overlayVersion++; overlay.Update("Escuchando · 00:00"); timer.Start();
                SetStatus("Te estamos escuchando", "Pulsa de nuevo el atajo para transcribir. Máximo 10 minutos por dictado.");
                return;
            }
            busy = true; timer.Stop(); duration.Stop();
            overlay.Update("Transcribiendo…", busy: true);
            SetStatus("Convirtiendo tu voz en texto", "El procesamiento se realiza en tu equipo. Puede tardar unos segundos.");
            var captured = recorder;
            byte[] audio;
            bool hasSpeech;
            try { audio = await captured.StopAsync(); hasSpeech = captured.HasSpeech; }
            finally { captured.Dispose(); recorder = null; }
            if (!hasSpeech || duration.Elapsed.TotalSeconds < 0.5)
            {
                SetStatus("No se detectó voz", "Comprueba el micrófono seleccionado y vuelve a intentarlo.");
                await ShowOverlayMessage("No se detectó voz"); return;
            }
            var text = await transcriber.TranscribeAsync(storage.ModelPath, audio, storage.Settings.Language, lifetime.Token, PersonalVocabulary.Prompt(storage.Vocabulary));
            if (exiting) return;
            if (string.IsNullOrWhiteSpace(text)) { SetStatus("No se obtuvo texto", "Intenta hablar más cerca del micrófono."); await ShowOverlayMessage("Sin texto reconocido"); return; }
            var rawText = text;
            string? polishWarning = null;
            if (storage.Settings.CleanWithAi)
            {
                overlay.Update("Puliendo tu escritura…", busy: true);
                SetStatus("Dando forma a tus palabras", "DeepSeek está limpiando muletillas y puntuación, conservando el contenido.");
                try { text = await polisher.PolishAsync(text, SecretStore.Unprotect(storage.Settings.ProtectedApiKey), storage.Settings.ApiModel, storage.Vocabulary, lifetime.Token); }
                catch (OperationCanceledException) when (exiting) { throw; }
                catch (Exception ex) { polishWarning = "Se conservó el texto original. La limpieza con IA falló: " + ex.Message; }
            }
            if (exiting) return;
            lastText = text;
            lastDictation = new Dictation(text, DateTime.Now, duration.Elapsed.TotalSeconds) { OriginalText = rawText };
            string? saveWarning = null;
            try { storage.Add(lastDictation); }
            catch (Exception ex) { saveWarning = "No se pudo guardar el historial: " + ex.Message; }
            RefreshHistory();
            var copied = await Native.CopyAsync(text);
            var pasted = copied && storage.Settings.AutoPaste && await Native.PasteAsync(target, text);
            SetStatus(pasted ? "Dictado enviado" : copied ? "Dictado copiado" : "Dictado listo", saveWarning ?? polishWarning ?? (pasted ? "Texto enviado a la aplicación de origen. También está en el portapapeles." : copied ? "Pulsa Ctrl+V en el campo de destino para pegarlo." : "El portapapeles está ocupado. Copia el texto desde el historial o la bandeja."));
            if (polishWarning != null) tray.ShowBalloonTip(5000, "Dictado sin limpieza de IA", polishWarning, Forms.ToolTipIcon.Warning);
            await ShowOverlayMessage(polishWarning != null ? "Texto original · IA no disponible" : pasted ? "✓ Texto enviado" : copied ? "✓ Copiado · usa Ctrl+V" : "Texto listo · abre Notaker");
        }
        catch (OperationCanceledException) when (exiting) { }
        catch (Exception ex)
        {
            timer.Stop(); duration.Stop(); recorder?.Dispose(); recorder = null; overlay.Hide();
            SetStatus("No se pudo completar el dictado", FriendlyError(ex));
            if (!exiting) { tray.ShowBalloonTip(5000, "Notaker", FriendlyError(ex), Forms.ToolTipIcon.Warning); ShowMain(); }
        }
        finally { busy = false; if (recorder == null) { SettingsPanel.IsEnabled = true; LiveText.Text = ""; } }
    }
    private void RecordingFailed(Exception ex)
    {
        if (busy || exiting || recorder == null) return;
        timer.Stop(); duration.Stop(); recorder.Dispose(); recorder = null;
        SettingsPanel.IsEnabled = true; overlay.Hide(); SetStatus("Micrófono interrumpido", ex.Message); ShowMain();
    }
    private async Task ShowOverlayMessage(string text)
    {
        var version = ++overlayVersion; overlay.Update(text);
        await Task.Delay(1700);
        if (version == overlayVersion && !exiting) overlay.Hide();
    }
    private static string FriendlyError(Exception ex) => ex is DllNotFoundException || ex.Message.Contains("native", StringComparison.OrdinalIgnoreCase)
        ? "No se pudo cargar el motor local. Instala Microsoft Visual C++ Redistributable 2022 x64 y vuelve a intentarlo. " + ex.Message
        : ex.Message;
    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        busy = true; SettingsPanel.IsEnabled = false; SetupButton.IsEnabled = false; DownloadProgress.Visibility = Visibility.Visible;
        SetStatus("Preparando tu voz", "Descargando el modelo multilingüe. Esta descarga solo se necesita una vez.");
        operation = DownloadAsync();
        await operation;
    }
    private async Task DownloadAsync()
    {
        try
        {
            var progress = new Progress<double>(value => { DownloadProgress.IsIndeterminate = value < 0; if (value >= 0) DownloadProgress.Value = value; LiveText.Text = value < 0 ? "Descargando…" : $"Descargando · {value:0}%"; });
            await Task.Run(() => Transcriber.DownloadModelAsync(storage.ModelPath, storage.Settings.Model, progress, lifetime.Token));
            RefreshStatus();
        }
        catch (OperationCanceledException) when (exiting) { }
        catch (Exception ex) { SetStatus("No se pudo preparar el modelo", FriendlyError(ex)); }
        finally { busy = false; SettingsPanel.IsEnabled = true; SetupButton.IsEnabled = true; DownloadProgress.Visibility = Visibility.Collapsed; }
    }
    private void Settings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        storage.Settings.Language = LanguageBox.SelectedIndex == 1 ? "es" : LanguageBox.SelectedIndex == 2 ? "en" : "auto";
        storage.Settings.Microphone = MicrophoneBox.SelectedIndex - 1; SavePreferences();
    }
    private void Model_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        storage.Settings.Model = ModelBox.SelectedIndex == 1 ? "small" : "base";
        SavePreferences(); RefreshStatus();
    }
    private void Hotkey_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ready || exiting) return;
        capturingShortcut = true;
        if (registered) { Native.Unregister(handle, hotkeyId); registered = false; }
        HotkeyHint.Text = "Pulsa tu combinación. Esc cancela; Tab sale del campo.";
    }
    private void Hotkey_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        capturingShortcut = false;
        if (!ready || exiting) return;
        if (!registered) registered = Native.Register(handle, storage.Settings.Shortcut, hotkeyId);
        HotkeyBox.Text = (pendingShortcut ?? storage.Settings.Shortcut).DisplayName;
        HotkeyHint.Text = pendingShortcut != null ? "Pulsa Guardar para aplicar esta combinación." : "Haz clic y pulsa una combinación de teclas.";
        if (!registered) SetStatus("Atajo no disponible", "Otra aplicación está usando el atajo guardado. Captura y guarda una combinación nueva.");
    }
    private void Hotkey_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            pendingShortcut = null; SaveHotkeyButton.IsEnabled = false;
            HotkeyBox.Text = storage.Settings.Shortcut.DisplayName;
            HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }
        var candidate = DictationShortcut.Capture(key, modifiers);
        if (candidate == null)
        {
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                HotkeyHint.Text = "Mantén esas teclas y pulsa la tecla principal.";
            else HotkeyHint.Text = "Usa Ctrl, Alt, Shift o Win + otra tecla, o una tecla F (excepto F12).";
            return;
        }
        pendingShortcut = candidate;
        HotkeyBox.Text = candidate.DisplayName;
        SaveHotkeyButton.IsEnabled = true;
        HotkeyHint.Text = "Combinación capturada. Pulsa Guardar para aplicarla.";
    }
    private void SaveHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (pendingShortcut == null || busy || recorder != null) return;
        // Keyboard activation must leave capture mode before registering the shortcut.
        if (capturingShortcut) SaveHotkeyButton.Focus();
        var choice = pendingShortcut;
        if (choice == storage.Settings.Shortcut && registered)
        {
            pendingShortcut = null; SaveHotkeyButton.IsEnabled = false;
            HotkeyHint.Text = "Este atajo ya está guardado.";
            return;
        }
        var newId = hotkeyId == 1 ? 2 : 1;
        if (!Native.Register(handle, choice, newId))
        {
            HotkeyHint.Text = "Atajo ocupado o reservado por Windows. Prueba otro.";
            return;
        }
        var previous = storage.Settings.CustomHotkey;
        try
        {
            storage.Settings.CustomHotkey = choice;
            storage.SaveSettings();
        }
        catch (Exception ex)
        {
            storage.Settings.CustomHotkey = previous;
            Native.Unregister(handle, newId);
            SetStatus("No se pudo guardar el atajo", ex.Message);
            return;
        }
        if (registered) Native.Unregister(handle, hotkeyId);
        registered = true; hotkeyId = newId; pendingShortcut = null;
        SaveHotkeyButton.IsEnabled = false; RefreshStatus();
        HotkeyHint.Text = "Guardado: " + choice.DisplayName;
    }
    private void Preference_Click(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        storage.Settings.AutoPaste = PasteBox.IsChecked == true;
        storage.Settings.KeepHistory = HistoryBox.IsChecked == true; SavePreferences();
    }
    private void SavePreferences() { try { storage.SaveSettings(); } catch (Exception ex) { SetStatus("No se pudieron guardar los ajustes", ex.Message); } }
    private void WritingSettings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recorder != null) return;
        var dialog = new WritingSettingsWindow(storage) { Owner = this };
        if (dialog.ShowDialog() == true) RefreshStatus();
    }
    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recorder != null) { SetStatus("Termina el dictado primero", "Puedes actualizar cuando no haya una grabación o transcripción en curso."); return; }
        new UpdateWindow(storage, ExitAsync) { Owner = this }.ShowDialog();
    }
    private void Correct_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Dictation entry) Correct(entry);
    }
    private void Correct(Dictation entry)
    {
        if (busy || recorder != null) return;
        ShowMain();
        var dialog = new CorrectionWindow(entry, storage.Settings.LearnVocabulary) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var learned = storage.Correct(entry, dialog.Corrected);
            if (entry == lastDictation) { lastText = dialog.Corrected; lastDictation = entry with { Text = dialog.Corrected, OriginalText = entry.OriginalText ?? entry.Text }; }
            RefreshHistory(); SetStatus("Corrección guardada", learned > 0 ? $"{learned} palabras añadidas a tu vocabulario. Puedes revisarlas en Escritura y vocabulario." : "Puedes copiar el texto corregido. El vocabulario personal se usa en próximos dictados.");
        }
        catch (Exception ex) { SetStatus("No se pudo guardar la corrección", ex.Message); }
    }
    private void RefreshHistory() { HistoryList.ItemsSource = null; HistoryList.ItemsSource = storage.History; EmptyHistory.Visibility = storage.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private async void Copy_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is Dictation entry) SetStatus(await Native.CopyAsync(entry.Text) ? "Texto copiado" : "Portapapeles ocupado", "Puedes pegar el dictado en cualquier aplicación con Ctrl+V."); }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        try { storage.ClearHistory(); RefreshHistory(); } catch (Exception ex) { SetStatus("No se pudo borrar el historial", ex.Message); }
    }
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true; Hide();
        tray.ShowBalloonTip(2500, "Notaker sigue activo", "Usa el atajo para dictar. Para cerrar, elige Salir en la bandeja.", Forms.ToolTipIcon.Info);
    }
    internal async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); lifetime.Cancel();
        if (registered) Native.Unregister(handle, hotkeyId);
        if (recorder != null && !busy) { try { await recorder.StopAsync(); } catch { } recorder.Dispose(); recorder = null; }
        if (operation != null) { try { await operation; } catch { } }
        source?.RemoveHook(WindowMessage); overlay.Close(); tray.Dispose(); transcriber.Dispose(); polisher.Dispose(); lifetime.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
