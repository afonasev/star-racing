namespace StarRacingPrototype.Distribution;

// Policy is shared by the real entry and focused refusal/failure scenarios.
public sealed class LauncherStartup {
    public int ExitCode { get; private set; }
    public async Task Run(Func<bool> confirmed, Func<Task<string>> discover, Func<string, Task<bool>> consent,
        Func<string, Task> activate, Func<Task<int>> play, Action<Exception> log, Func<Exception, Task> failedUpdate = null) {
        bool accepted = false;
        try {
            if (confirmed()) {
                var version = await discover();
                if (version != null && await consent(version)) {
                    accepted = true;
                    await activate(version);
                    return; // Agent owns the next Player; never launch a second one.
                }
            }
        } catch (Exception error) {
            try { log(error); } catch { /* Logging cannot prevent offline play. */ }
            if (accepted && failedUpdate != null) { try { await failedUpdate(error); } catch { } }
        }
        ExitCode = await play();
    }
}
