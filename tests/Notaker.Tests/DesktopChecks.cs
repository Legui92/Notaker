using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Notaker;

internal static class DesktopChecks
{
    internal static Task RunAsync(bool simulateFocusFailure = false)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
            var app = new Application();
            var editor = new TextBox { AcceptsReturn = true, Margin = new Thickness(20) };
            var window = new Window { Title = "Notaker automated paste test", Width = 450, Height = 200, Content = editor };
            window.Loaded += async (_, _) =>
            {
                IDataObject? previous = null;
                var capturedClipboard = false;
                var hwnd = new WindowInteropHelper(window).Handle;
                var overlay = new DictationOverlay();
                const string sample = "Prueba: reunión con Luzia.\nEnglish dictation works.";
                try
                {
                    previous = Clipboard.GetDataObject(); capturedClipboard = true;
                    window.Activate(); editor.Focus();
                    await Task.Delay(250);
                    if (simulateFocusFailure || Native.GetForegroundWindow() != hwnd)
                        throw new Exception("Test window could not obtain foreground focus. Run desktop checks in an interactive Windows session with permission to activate the test window.");
                    DictationShortcut? selected = null;
                    foreach (var shortcut in new[] { new DictationShortcut(6, 0x78), new DictationShortcut(3, 0x4B), new DictationShortcut(6, 0x4A) })
                        if (Native.Register(hwnd, shortcut, 88)) { selected = shortcut; break; }
                    if (selected == null) throw new Exception("No free test hotkey.");
                    if (Native.Register(hwnd, selected, 89)) throw new Exception("Duplicate hotkey was unexpectedly accepted.");
                    Console.WriteLine("PASS: Global hotkey registration detects conflicts");
                    overlay.Update("Prueba de indicador", 0.03f);
                    await Task.Delay(150);
                    if (Native.GetForegroundWindow() != hwnd) throw new Exception("Overlay stole focus.");
                    Console.WriteLine("PASS: Dictation overlay does not steal cursor focus");
                    if (!await Native.PasteAsync(hwnd, sample)) throw new Exception("SendInput could not send paste.");
                    await Task.Delay(250);
                    if (editor.Text.Replace("\r\n", "\n") != sample) throw new Exception("Text did not reach focused editor: " + editor.Text);
                    Console.WriteLine("PASS: Unicode multiline dictation reaches cursor through real Ctrl+V");
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    try
                    {
                    Native.Unregister(hwnd, 88); Native.Unregister(hwnd, 89);
                    overlay.Close();
                    try
                    {
                        if (capturedClipboard && Clipboard.ContainsText() && Clipboard.GetText() == sample)
                        {
                            if (previous != null) Clipboard.SetDataObject(previous, true); else Clipboard.Clear();
                        }
                    }
                    catch { /* Clipboard may be owned by another application by now. */ }
                    window.Close();
                    }
                    catch (Exception ex) { failure ??= ex; }
                    finally { app.Shutdown(); }
                }
            };
            app.Run(window);
            }
            catch (Exception ex) { failure ??= ex; }
            finally
            {
                // Report only after windows and the dispatcher have finished closing.
                if (failure != null) completion.TrySetException(failure);
                else completion.TrySetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
