using System.IO;
using Microsoft.Win32;
using Notaker;

static class StartupChecks
{
    public static void Run(string root)
    {
        var keyPath = @"Software\Notaker.Tests\Startup-" + Guid.NewGuid().ToString("N");
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
        }
        try
        {
            var exe = Path.Combine(root, "Folder with spaces", "Notaker.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "test fixture, never executed");
            var startup = new StartupRegistration(exe, keyPath);
            Check(!startup.Read().Registered, "Startup is opt-in and reading does not register the app");
            startup.SetRegistered(true);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath + @"\Run"))
                Check((string?)key?.GetValue("Notaker") == $"\"{exe}\" --startup", "Startup command quotes paths with spaces and uses startup argument");
            Check(new StartupRegistration(exe, keyPath).Read() is { Enabled: true, CurrentExecutable: true }, "Startup registration persists independently of app preferences");
            using (var approved = Registry.CurrentUser.CreateSubKey(keyPath + @"\Explorer\StartupApproved\Run"))
                approved.SetValue("Notaker", new byte[] { 3,0,0,0,1,2,3,4,5,6,7,8 }, RegistryValueKind.Binary);
            Check(!startup.Read().Enabled && startup.Read().DisabledByWindows, "Startup reads Windows external disable state");
            startup.SetRegistered(true);
            Check(startup.Read().DisabledByWindows, "Re-registering cannot override Windows user disable choice");
            Check(!new StartupRegistration(Path.Combine(root, "Notaker.exe"), keyPath).Read().CurrentExecutable, "Startup detects registration pointing to another executable");
            startup.SetRegistered(false);
            Check(!startup.Read().Registered, "Disabling removes only the Notaker Run entry");
            using (var approved = Registry.CurrentUser.OpenSubKey(keyPath + @"\Explorer\StartupApproved\Run"))
                Check(((byte[])approved!.GetValue("Notaker")!)[4] == 1, "Windows approval and disable timestamp remain untouched");
            var rejected = false;
            try { new StartupRegistration(Path.Combine(root, "Missing", "Notaker.exe"), keyPath).SetRegistered(true); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !startup.Read().Registered, "Missing executable cannot become a broken startup entry");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); }
    }
}
