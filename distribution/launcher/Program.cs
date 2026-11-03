using System.Diagnostics;
using Newtonsoft.Json;
using StarRacingPrototype.Distribution;
using Velopack;
using Velopack.Locators;

try {
    var content = AppContext.BaseDirectory;
    // macOS self-contained runtimes live below the universal native entry.
    if (!File.Exists(Path.Combine(content, "launcher.json")))
        content = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../.."));
    var entry = Path.Combine(content, OperatingSystem.IsWindows() ? "StarRacingLauncher.exe" : "StarRacingLauncher");
    var locator = VelopackLocator.CreateDefaultForPlatform(new LauncherProcess(entry));
    VelopackApp.Build().SetLocator(locator).SetAutoApplyOnStartup(false).Run();
    var config = JsonConvert.DeserializeObject<LauncherConfig>(File.ReadAllText(Path.Combine(content, "launcher.json")))
        ?? throw new InvalidDataException("Missing launcher configuration");
    config.Validate();
    if (locator.AppId != config.AppId || locator.Channel != config.Channel || locator.CurrentlyInstalledVersion?.ToNormalizedString() != config.Version)
        throw new InvalidDataException($"Launcher identity does not match installed package: id={locator.AppId}, channel={locator.Channel}, version={locator.CurrentlyInstalledVersion}, root={locator.RootAppDir}");
    var player = Path.Combine(content, config.Player);
    if (!File.Exists(player)) throw new FileNotFoundException("Не найден Player Star Racing. Восстановите установку.", player);
    using var installGate = OperatingSystem.IsWindows() ? new Mutex(false, @"Global\StarRacingDesktop-Running") : null;
    if (installGate != null) {
        try { if (!installGate.WaitOne(1000)) throw new IOException("Установка Star Racing ещё выполняется"); }
        catch (AbandonedMutexException) { }
        installGate.ReleaseMutex();
    }
    var state = LauncherState.DirectoryFor(content, config.Track);
    Directory.CreateDirectory(state);
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    // Separate installations never share working confirmations or sequence floors.
    var working = LauncherState.ReadWorking(Path.Combine(state, "working.json"), config.Version);
    using var source = new AuthenticatedUpdateSource(config.AppId, config.Channel, 0, config.Track);
    bool recoveryLaunch=false;
    var recoveryPath=Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_LAUNCHER");
    if(!string.IsNullOrEmpty(recoveryPath)) {
        var recovery=RecoveryTransaction.Read(recoveryPath);
        if(recovery.state!="pending-health"||recovery.targetVersion!=config.Version||recovery.token!=Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_TOKEN")||!string.Equals(Path.GetFullPath(recovery.executable),Path.GetFullPath(player),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new InvalidDataException("Invalid recovery launcher invocation");
        recoveryLaunch=true;
    }
    var coordinator = new LauncherStartup();
    await coordinator.Run(
        () => !recoveryLaunch && working != null && !LauncherRecovery.Pending(working.DataPath, config.Track),
        async () => {
            source.MinimumSequence=LauncherState.Sequence(state);
            await source.GetReleaseFeed(null, config.AppId, config.Channel);
            LauncherState.SaveSequence(state, source.Release.sequence);
            return Velopack.SemanticVersion.Parse(source.Release.version) > Velopack.SemanticVersion.Parse(config.Version)
                && !LauncherRecovery.Failed(working.DataPath, config.Track, source.Release.version) ? source.Release.version : null;
        },
        BootstrapPrompt.Confirm,
        async version => {
            if (source.Release.version != version) throw new InvalidDataException("Выбранная версия изменилась");
            await LauncherRecovery.PrepareAndStart(content, player, args, config, locator, working.DataPath, source);
        },
        async () => await LauncherState.RunPlayer(player, args, config.Version, state),
        error => File.AppendAllText(Path.Combine(state, "launcher.log"), DateTime.UtcNow.ToString("O") + " " + error + Environment.NewLine),
        error => error is OperationCanceledException ? Task.CompletedTask : BootstrapPrompt.Failure("Обновление не установлено. Будет запущена текущая игра. Проверьте подключение, свободное место и права доступа."));
    return coordinator.ExitCode;
} catch (Exception error) {
    Console.Error.WriteLine(error);
    try { await BootstrapPrompt.Failure("Не удалось запустить Star Racing. Восстановите приложение с помощью установщика. Пользовательские данные сохранятся."); } catch { }
    return 1;
}

sealed class LauncherProcess(string entry) : IProcessImpl {
    public string GetCurrentProcessPath() => entry;
    public uint GetCurrentProcessId() => checked((uint)Environment.ProcessId);
    public void StartProcess(string exe, IEnumerable<string> arguments, string directory, bool showWindow) {
        var start = new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = !showWindow };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Cannot start updater");
    }
    public void Exit(int code) => Environment.Exit(code);
}
