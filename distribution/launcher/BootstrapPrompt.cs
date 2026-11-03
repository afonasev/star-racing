using System.Diagnostics;

namespace StarRacingPrototype.Distribution;

public static class BootstrapPrompt
{
    public static async Task Failure(string message)
    {
        if (OperatingSystem.IsWindows()) { ShowError(IntPtr.Zero,message,"Star Racing — ошибка запуска",0x10 | 0x10000);return; }
        if (!OperatingSystem.IsMacOS()) return;
        var start=new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("on run argv\ndisplay dialog (item 1 of argv) with title \"Star Racing\" buttons {\"OK\"} default button \"OK\"\nend run");
        start.ArgumentList.Add("--");start.ArgumentList.Add(message);
        using var child=Process.Start(start) ?? throw new IOException("Cannot show launch error");
        await child.WaitForExitAsync();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,EntryPoint="MessageBoxW")]
    static extern int ShowError(IntPtr window,string text,string title,uint type);

    public static async Task<bool> Confirm(string version)
    {
        // Never interpolate signed/server text into an executable script.
        var message = "Доступна новая версия Star Racing: " + version +
            ". Установить обновление перед запуском игры?";
        if (OperatingSystem.IsWindows())
            return await BootstrapWindowsDialog.Confirm(version, message);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Update consent UI requires Windows or macOS");
        var start = new ProcessStartInfo("/usr/bin/osascript")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("on run argv\nset answer to display dialog (item 1 of argv) with title \"Star Racing — обновление\" buttons {\"Играть сейчас\", \"Обновить\"} default button \"Играть сейчас\"\nreturn button returned of answer\nend run");
        start.ArgumentList.Add("--"); start.ArgumentList.Add(message);
        using var process = Process.Start(start) ?? throw new IOException("Update confirmation did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var answer = (await output).Trim();
        if (process.ExitCode != 0) throw new IOException("Update confirmation unavailable: " + await error);
        return answer == "Обновить";
    }

}
