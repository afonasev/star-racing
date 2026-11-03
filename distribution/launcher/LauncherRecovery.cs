using System.Diagnostics;
using System.Runtime.InteropServices;
using Velopack.Locators;

namespace StarRacingPrototype.Distribution;

public static class LauncherRecovery {
    static string Journal(string dataPath, string track) => Path.Combine(dataPath, "desktop-recovery-" + track, "transaction.json");
    public static bool Pending(string dataPath, string track) {
        var path = Journal(dataPath, track);
        if (!File.Exists(path)) return false;
        var state = RecoveryTransaction.Read(path).state;
        return state != "healthy" && state != "recovery-required";
    }
    public static bool Failed(string dataPath, string track, string version) {
        var path = Journal(dataPath, track);
        if (!File.Exists(path)) return false;
        var record = RecoveryTransaction.Read(path);
        return record.targetVersion == version && record.state == "recovery-required";
    }
    public static async Task PrepareAndStart(string content, string player, string[] arguments, LauncherConfig config,
        IVelopackLocator locator, string dataPath, AuthenticatedUpdateSource target) {
        var path = Journal(dataPath, config.Track);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Pending(dataPath, config.Track)) throw new IOException("Другое обновление ещё не завершено");
        var packages = Path.Combine(Path.GetDirectoryName(path)!, "packages");
        Directory.CreateDirectory(packages);
        using var progress = new LauncherProgress(content);
        var previousEnvelope = await target.RecoveryBaseline(config.Version, progress.Token);
        using var previous = new AuthenticatedUpdateSource(config.AppId, config.Channel, 0, config.Track);
        previous.Restore(previousEnvelope);
        var needed = checked(target.Release.size * 2 + previous.Release.size * 2 + 268435456);
        if (new DriveInfo(Path.GetPathRoot(packages)!).AvailableFreeSpace < needed) throw new IOException("Недостаточно места для обновления и восстановления");
        async Task Download(AuthenticatedUpdateSource source, string label) {
            progress.Report(label, 0);
            var destination = Path.Combine(packages, source.Release.fileName);
            if (File.Exists(destination)) {
                try { ReleaseAuthentication.VerifyFile(source.Release, destination); return; }
                catch (InvalidDataException) { File.Delete(destination); }
            }
            source.DownloadConsent = true;
            try { await source.DownloadReleaseEntry(null, source.Release.Asset, destination, value => progress.Report(label, value), progress.Token); }
            finally { source.DownloadConsent = false; }
        }
        await Download(previous, "Подготовка пакета восстановления");
        await Download(target, "Загрузка обновления");
        progress.Report("Проверка и подготовка установки…", 100);
        var runtime = config.Channel == "win-x64" ? "win-x64" : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        // Unique retained copy cannot be overwritten by an interrupted earlier preparation.
        var agent = Path.Combine(Path.GetDirectoryName(path)!, "agent-launcher-" + Guid.NewGuid().ToString("N"));
        CopyTree(Path.Combine(content, "recovery", runtime), agent);
        var helper = Path.Combine(agent, Path.GetFileName(locator.UpdateExePath));
        CopyFile(locator.UpdateExePath, helper);
        using var process = Process.GetCurrentProcess();
        var record = new RecoveryRecord {
            appId = config.AppId, channel = config.Channel, track = config.Track,
            previousVersion = config.Version, targetVersion = target.Release.version,
            previousEnvelope = previousEnvelope, targetEnvelope = target.Envelope,
            root = locator.RootAppDir, packages = packages, helper = helper, executable = player,
            parentPid = process.Id, parentStarted = process.StartTime.ToUniversalTime().Ticks,
            arguments = arguments, token = Guid.NewGuid().ToString("N")
        };
        RecoveryTransaction.Verified(record, true);
        RecoveryTransaction.Verified(record, false);
        var executable = Path.Combine(agent, config.Channel == "win-x64" ? "StarRacingRecovery.exe" : "StarRacingRecovery");
        if (!File.Exists(executable)) throw new IOException("Не найден компонент восстановления");
        progress.Token.ThrowIfCancellationRequested();
        RecoveryTransaction.Save(path, record);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = agent, CreateNoWindow = true };
        start.ArgumentList.Add(path); start.ArgumentList.Add(record.token);
        ownership.Dispose(); // Child must acquire the journal before any apply work.
        using var child = Process.Start(start) ?? throw new IOException("Не удалось запустить восстановление");
    }
    static void CopyFile(string from, string to) {
        File.Copy(from, to, false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(to, File.GetUnixFileMode(from));
    }
    static void CopyTree(string from, string to) {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) CopyFile(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(from)) CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
}
