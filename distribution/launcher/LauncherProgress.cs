using System.Diagnostics;

namespace StarRacingPrototype.Distribution;

public sealed class LauncherProgress : IDisposable {
    readonly CancellationTokenSource cancellation = new();
    readonly Process mac;
    public CancellationToken Token => cancellation.Token;
    public volatile bool Finished;
    public int Percent;
    public string Message = "Подготовка пакета восстановления…";
    public LauncherProgress(string content) {
        if (OperatingSystem.IsWindows()) _ = BootstrapWindowsDialog.Progress(this);
        else if (OperatingSystem.IsMacOS()) {
            var start = new ProcessStartInfo(Path.Combine(content, "StarRacingProgress")) {
                UseShellExecute=false, RedirectStandardInput=true, RedirectStandardOutput=true, CreateNoWindow=true
            };
            mac=Process.Start(start) ?? throw new IOException("Не удалось открыть окно загрузки");
            mac.Exited += (_, _) => { if (!Finished) Cancel(); };
            mac.EnableRaisingEvents=true;
        }
    }
    public void Cancel() { if (!Finished) cancellation.Cancel(); }
    public void Report(string message, int percent) {
        Message=message; Interlocked.Exchange(ref Percent,Math.Clamp(percent,0,100));
        if (mac != null && !mac.HasExited) {
            try { mac.StandardInput.WriteLine(Percent+"\t"+message.Replace('\n',' ')); mac.StandardInput.Flush(); }
            catch (IOException) { Cancel(); }
        }
        Token.ThrowIfCancellationRequested();
    }
    public void Dispose() {
        Finished=true;
        if (mac != null) {
            try { if (!mac.HasExited) { mac.StandardInput.Close(); mac.WaitForExit(2000); if (!mac.HasExited) mac.Kill(); } }
            catch (InvalidOperationException) { }
            mac.Dispose();
        }
        // Windows callback may still read the source until the next native timer tick.
    }
}
