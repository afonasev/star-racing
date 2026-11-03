using Newtonsoft.Json;
using StarRacingPrototype.Distribution;

int assertions = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
async Task Scenario(bool healthy, string version, bool consent, string failure, string expected) {
    var events = new List<string>(); var runner = new LauncherStartup();
    await runner.Run(() => healthy,
        () => { events.Add("check"); if (failure == "check") throw new IOException("offline"); return Task.FromResult(version); },
        value => { events.Add("consent:"+value); if (failure == "consent") throw new IOException("dialog"); return Task.FromResult(consent); },
        value => { events.Add("activate:"+value); if (failure == "activate") throw new IOException("tampered package"); return Task.CompletedTask; },
        () => { events.Add("play"); return Task.FromResult(37); },
        _ => events.Add("error"));
    Check(string.Join(',', events) == expected, "unexpected startup route: " + string.Join(',', events));
    Check(runner.ExitCode == (events.Contains("play") ? 37 : 0), "actual Player exit code is preserved");
}
await Scenario(false,"2.0.0",true,"","play");
await Scenario(true,null,false,"","check,play");
await Scenario(true,"2.0.0",false,"","check,consent:2.0.0,play");
await Scenario(true,"2.0.0",true,"","check,consent:2.0.0,activate:2.0.0");
await Scenario(true,"2.0.0",true,"check","check,error,play");
await Scenario(true,"2.0.0",true,"consent","check,consent:2.0.0,error,play");
await Scenario(true,"2.0.0",true,"activate","check,consent:2.0.0,activate:2.0.0,error,play");
var root = Path.Combine(Path.GetTempPath(), "star-racing-launcher-checks-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try {
    var ready = new LauncherReady { Version="1.0.0", Token="token", DataPath=root, Pid=123, Started=456 };
    Check(LauncherState.Matches(ready,"1.0.0","token",123,456),"exact readiness accepted");
    Check(!LauncherState.Matches(ready,"2.0.0","token",123,456),"wrong version rejected");
    Check(!LauncherState.Matches(ready,"1.0.0","stale",123,456),"wrong token rejected");
    Check(!LauncherState.Matches(ready,"1.0.0","token",124,456),"wrong process rejected");
    Check(!LauncherState.Matches(ready,"1.0.0","token",123,456+TimeSpan.TicksPerSecond*2),"PID reuse rejected");
    Check(LauncherState.ReadWorking(Path.Combine(root,"missing"),"1.0.0")==null,"fresh install unconfirmed");
    LauncherState.SaveSequence(root,5); LauncherState.SaveSequence(root,3);
    Check(LauncherState.Sequence(root)==5,"monotonic authenticated sequence floor");
    var working=Path.Combine(root,"working.json");
    File.WriteAllText(working, JsonConvert.SerializeObject(new LauncherWorking { Version="1.0.0", DataPath=root }));
    Check(LauncherState.ReadWorking(working,"2.0.0")==null,"prior release health cannot confirm another version");
    Check(LauncherState.ReadWorking(working,"1.0.0")!=null,"working version retained");
    Check(LauncherState.DirectoryFor(root,"test")!=LauncherState.DirectoryFor(root,"production"),"channel state separated");
    Check(LauncherState.DirectoryFor(root,"test")!=LauncherState.DirectoryFor(root+"other","test"),"installation state separated");
    if (!OperatingSystem.IsWindows()) {
        var player=Path.Combine(root,"fake-player");
        File.WriteAllText(player,"#!/usr/bin/env python3\nimport os,sys,json,time\npid=os.getpid()\n# macOS process start wall time is bounded by the parent identity tolerance.\npayload=dict(Version=os.environ['STAR_RACING_LAUNCHER_VERSION'],Token=os.environ['STAR_RACING_LAUNCHER_TOKEN'],Pid=pid,Started=621355968000000000+int(time.time()*10000000),DataPath=os.path.dirname(__file__))\nwith open(os.environ['STAR_RACING_LAUNCHER_READY'],'w') as f: json.dump(payload,f)\nwith open(os.path.join(os.path.dirname(__file__),'argv.json'),'w') as f:json.dump(sys.argv[1:],f)\ntime.sleep(0.4)\nsys.exit(23)\n");
        File.SetUnixFileMode(player,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        File.Delete(working);
        Check(await LauncherState.RunPlayer(player,new[]{"-noaudio","-muted","argument with spaces","quote\"value"},"1.0.0",root)==23,"real child exit status");
        Check(File.ReadAllText(Path.Combine(root,"argv.json")).Contains("argument with spaces"),"argv passed without shell splitting");
        Check(File.ReadAllText(Path.Combine(root,"argv.json")).Contains("-muted"),"mute argument retained");
        Check(LauncherState.ReadWorking(working,"1.0.0")!=null,"real child readiness stored");
        Check(!Directory.EnumerateFiles(root,"ready-*.json").Any(),"one-time health files cleaned");
        using var supervisor=System.Diagnostics.Process.GetCurrentProcess();
        var record=new RecoveryRecord {
            state="pending-health",appId="tech.afonasev.star-racing.osx-universal",channel="osx-universal",track="production",
            previousVersion="0.9.0",targetVersion="1.0.0",root=root,packages=root,helper=player,executable=player,
            token=Guid.NewGuid().ToString("N"),launcherPid=supervisor.Id,launcherStarted=supervisor.StartTime.ToUniversalTime().Ticks
        };
        var journal=Path.Combine(root,"recovery.json");RecoveryTransaction.Save(journal,record);
        var oldPath=Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_LAUNCHER");
        var oldToken=Environment.GetEnvironmentVariable("STAR_RACING_RECOVERY_TOKEN");
        try {
            Environment.SetEnvironmentVariable("STAR_RACING_RECOVERY_LAUNCHER",journal);
            Environment.SetEnvironmentVariable("STAR_RACING_RECOVERY_TOKEN",record.token);
            Check(await LauncherState.RunPlayer(player,new[]{"-noaudio","-muted"},"1.0.0",root)==23,"recovery launches actual child through launcher");
            var child=RecoveryTransaction.Child(journal,record);
            Check(child.pid>0&&child.pid!=record.launcherPid,"recovery receives the distinct Player PID");
            Check(child.token==record.token&&child.version==record.targetVersion,"child handshake binds token and target");
            record.token=Guid.NewGuid().ToString("N");
            bool rejected=false;try{RecoveryTransaction.Child(journal,record);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"stale launcher child token cannot authorize health");
        } finally {
            Environment.SetEnvironmentVariable("STAR_RACING_RECOVERY_LAUNCHER",oldPath);
            Environment.SetEnvironmentVariable("STAR_RACING_RECOVERY_TOKEN",oldToken);
        }

    }
} finally { Directory.Delete(root,true); }
Console.WriteLine($"LAUNCHER_CHECKS_PASS {assertions}");
