using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Velopack;

namespace StarRacingPrototype.Distribution;

public sealed class LauncherConfig {
    public int Schema = 1;
    public string Version, Track, Channel, Player;
    public string AppId => "tech.afonasev.star-racing." + Channel;
    public void Validate() {
        if (Schema != 1 || Track != "test" && Track != "production" || Channel != "win-x64" && Channel != "osx-universal"
            || !SemanticVersion.TryParse(Version, out var version) || version.ToNormalizedString() != Version
            || (Track == "test") != Version.Contains('-') || Player != (Channel == "win-x64" ? "Star Racing.exe" : "Star Racing"))
            throw new InvalidDataException("Invalid Star Racing launcher identity");
    }
}
public sealed class LauncherWorking { public string Version, DataPath; }
public sealed class LauncherReady { public string Version, Token, DataPath; public int Pid; public long Started; }

public static class LauncherState {
    public static string DirectoryFor(string content, string track) {
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(content))));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarRacing", "launcher", track, identity);
    }
    public static LauncherWorking ReadWorking(string path, string version) {
        try {
            var working = JsonConvert.DeserializeObject<LauncherWorking>(File.ReadAllText(path));
            return working?.Version == version && Path.IsPathRooted(working.DataPath) && Directory.Exists(working.DataPath) ? working : null;
        } catch (Exception error) when (error is IOException || error is JsonException || error is ArgumentException) { return null; }
    }
    public static long Sequence(string state) {
        var file = Path.Combine(state, "sequence");
        if (!File.Exists(file)) return 0;
        if (!long.TryParse(File.ReadAllText(file), out var value) || value < 0 || value > int.MaxValue) throw new InvalidDataException("Invalid sequence floor");
        return value;
    }
    public static void SaveSequence(string state, long sequence) {
        using var lease=new FileStream(Path.Combine(state,"sequence.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        RecoveryTransaction.Atomic(Path.Combine(state, "sequence"), Math.Max(Sequence(state), sequence).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public static bool Matches(LauncherReady ready, string version, string token, int pid, long started) =>
        ready != null && ready.Version == version && ready.Token == token && ready.Pid == pid
        && Math.Abs(ready.Started - started) <= TimeSpan.TicksPerSecond && Path.IsPathRooted(ready.DataPath) && Directory.Exists(ready.DataPath);

    public static async Task<int> RunPlayer(string player, string[] arguments, string version, string state,
        Func<string, Task> notify = null, TimeSpan? startupTimeout = null) {
        notify ??= BootstrapPrompt.Failure;
        var readyPath = Path.Combine(state, "ready-" + Guid.NewGuid().ToString("N") + ".json");
        var token = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(player) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(player) };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment.Remove("STAR_RACING_RECOVERY_LAUNCHER");
        start.Environment.Remove("STAR_RACING_RECOVERY_TOKEN");
        using(var supervisor=Process.GetCurrentProcess()) {
            start.Environment["STAR_RACING_LAUNCHER_PID"]=supervisor.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["STAR_RACING_LAUNCHER_STARTED"]=supervisor.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        start.Environment["STAR_RACING_LAUNCHER_PLAYER"] = "1";
        start.Environment["STAR_RACING_LAUNCHER_READY"] = readyPath;
        start.Environment["STAR_RACING_LAUNCHER_TOKEN"] = token;
        start.Environment["STAR_RACING_LAUNCHER_VERSION"] = version;
        var recoveryPath=Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_LAUNCHER");
        RecoveryRecord recovery=null;
        if(!string.IsNullOrEmpty(recoveryPath)) {
            recovery=RecoveryTransaction.Read(recoveryPath);
            if(recovery.state!="pending-health"||recovery.targetVersion!=version||recovery.token!=Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_TOKEN"))
                throw new InvalidDataException("Invalid recovery startup context");
        }
        using var game = Process.Start(start) ?? throw new IOException("Не удалось запустить Star Racing");
        var started = game.StartTime.ToUniversalTime().Ticks;
        if(recovery!=null) {
            using var launcher=Process.GetCurrentProcess();
            RecoveryTransaction.Atomic(recoveryPath+".child",JsonConvert.SerializeObject(new RecoveryChild {
                pid=game.Id,started=started,launcherPid=launcher.Id,launcherStarted=launcher.StartTime.ToUniversalTime().Ticks,version=version,token=recovery.token
            }));
        }
        var deadline = Stopwatch.StartNew();
        var timeout = startupTimeout ?? TimeSpan.FromMinutes(2);
        bool confirmed = false, warned = false;
        try {
            while (!game.HasExited) {
                if (File.Exists(readyPath)) {
                    try {
                        var ready = JsonConvert.DeserializeObject<LauncherReady>(File.ReadAllText(readyPath));
                        if (Matches(ready, version, token, game.Id, started)) {
                            RecoveryTransaction.Atomic(Path.Combine(state, "working.json"), JsonConvert.SerializeObject(new LauncherWorking { Version = version, DataPath = ready.DataPath }));
                            confirmed = true;
                            break;
                        }
                    } catch (Exception error) when (error is IOException || error is JsonException) { Console.Error.WriteLine(error.Message); }
                }
                if (!warned && deadline.Elapsed >= timeout) {
                    warned = true;
                    await notify("Игра запущена, но за две минуты не подтвердила готовность меню. Возможно, загрузка зависла.\n\nЕсли окно игры появилось, дождитесь загрузки. Если окна нет, закройте Star Racing через диспетчер задач и попробуйте снова. При повторном сбое передайте это сообщение в поддержку.\n\nКод: SR-START-TIMEOUT");
                }
                await Task.Delay(100);
            }
            // Timeout is an observation, never permission to kill or replace a live game.
            await game.WaitForExitAsync();
            if (!confirmed || game.ExitCode != 0)
                await notify((confirmed ? "Игра завершилась с ошибкой." : "Игра завершилась до готовности меню.") +
                    "\n\nПопробуйте запустить игру снова. Если сбой повторяется, выполните восстановление через установщик. Пользовательские данные сохранятся." +
                    $"\n\nКод: SR-PLAYER-EXIT (0x{game.ExitCode:X8})");
            return game.ExitCode;
        } finally { if (File.Exists(readyPath)) File.Delete(readyPath); }
    }
}
