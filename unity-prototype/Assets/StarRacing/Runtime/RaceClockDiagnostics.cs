using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace StarRacingPrototype
{
    // Separate opt-in observer: no Time writes, FTM calls, CPU collector or race input.
    public sealed class RaceClockDiagnostics : MonoBehaviour
    {
        public const string Argument = "--race-menu-clock-dir";
        [Serializable] public struct Sample
        {
            public int frame, screen;
            public double wall, realtime, unscaledTime;
            public float unscaledDelta, scale, captureDelta, fixedDelta, maximumDelta;
            public bool ready, focused, loading, started, inFixedStep, cpuEnabled;
        }
        [Serializable] public sealed class Report
        {
            public string version, failure, limitation = "Passive menu clock validity only; not game performance or human acceptance. 5%/100ms tolerance is diagnostic.";
            public bool completed, clockValid, editor;
            public double wallSeconds, realtimeSeconds, unscaledSeconds, integratedDeltaSeconds, toleranceSeconds;
            public int readyAt = -1;
            public List<Sample> samples = new List<Sample>(4096);
        }
        readonly Stopwatch watch = new Stopwatch();
        Report report;
        RaceDirector director;
        RaceMenu menu;
        string output;
        public bool Completed => report != null && report.completed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, Argument);
            if (at < 0) return;
            if (at + 1 >= args.Length) throw new ArgumentException("Menu clock output required");
            new GameObject("Opt-in menu clock").AddComponent<RaceClockDiagnostics>().Begin(args[at + 1]);
        }
        public void Begin(string directory)
        {
            if (report != null || !SilentStartup.Enabled || !Path.IsPathRooted(directory)) throw new InvalidOperationException("Fresh absolute silent menu probe required");
            Directory.CreateDirectory(directory); output = Path.Combine(directory, "menu-clock.json");
            if (File.Exists(output)) throw new IOException("Fresh menu clock output required");
            report = new Report { version = Application.version, editor = Application.isEditor }; watch.Start();
        }
        void Update()
        {
            if (report == null || report.completed) return;
            if (director == null) director = FindAnyObjectByType<RaceDirector>();
            if (menu == null && director != null) menu = director.GetComponent<RaceMenu>();
            bool ready = director != null && director.Ready && menu != null && menu.ScreenState == RaceMenuScreen.Main && !menu.Loading && !director.Started;
            var sample = new Sample { frame = Time.frameCount, wall = watch.Elapsed.TotalSeconds,
                realtime = Time.realtimeSinceStartupAsDouble, unscaledTime = Time.unscaledTimeAsDouble, unscaledDelta = Time.unscaledDeltaTime,
                scale = Time.timeScale, captureDelta = Time.captureDeltaTime, fixedDelta = Time.fixedDeltaTime, maximumDelta = Time.maximumDeltaTime,
                ready = ready, screen = menu == null ? -1 : (int)menu.ScreenState, loading = menu != null && menu.Loading,
                started = director != null && director.Started, focused = Application.isFocused, inFixedStep = Time.inFixedTimeStep, cpuEnabled = RaceCpuDiagnostics.Enabled };
            report.samples.Add(sample);
            if (ready && report.readyAt < 0) report.readyAt = report.samples.Count - 1;
            if (report.readyAt >= 0 && sample.wall - report.samples[report.readyAt].wall >= 10) Finish(null);
            else if (sample.wall >= 120 || report.samples.Count >= 8192) Finish("Menu clock deadline/sample bound");
        }
        void Finish(string failure)
        {
            report.failure = failure;
            if (failure == null) ValidateWindow(report);
            report.completed = true; watch.Stop(); File.WriteAllText(output, JsonUtility.ToJson(report, true));
            UnityEngine.Debug.Log("MENU_CLOCK_RESULT valid=" + report.clockValid + " wall=" + report.wallSeconds + " unity=" + report.unscaledSeconds);
            if (!Application.isEditor) Application.Quit(report.clockValid ? 0 : 2);
        }
        public static void ValidateWindow(Report report)
        {
            report.clockValid = false; report.failure = null; report.integratedDeltaSeconds = 0;
            if (report.readyAt >= 0 && report.samples.Count > report.readyAt + 1)
            {
                var first = report.samples[report.readyAt]; var last = report.samples[report.samples.Count - 1];
                report.wallSeconds = last.wall - first.wall; report.realtimeSeconds = last.realtime - first.realtime;
                report.unscaledSeconds = last.unscaledTime - first.unscaledTime;
                for (int i = report.readyAt + 1; i < report.samples.Count; i++) report.integratedDeltaSeconds += report.samples[i].unscaledDelta;
                report.toleranceSeconds = Math.Max(.1, report.wallSeconds * .05);
                report.clockValid = Math.Abs(report.realtimeSeconds - report.wallSeconds) <= report.toleranceSeconds
                    && Math.Abs(report.unscaledSeconds - report.wallSeconds) <= report.toleranceSeconds
                    && Math.Abs(report.integratedDeltaSeconds - report.wallSeconds) <= report.toleranceSeconds;
                for (int i = report.readyAt; i < report.samples.Count; i++) {
                    var s = report.samples[i];
                    // A ready main menu normally holds the simulation at scale zero.
                    // Validate the unchanged window state, not a race-only scale of one.
                    if (!s.ready || !s.focused || s.inFixedStep || s.scale != first.scale || s.captureDelta != 0) { report.clockValid = false; report.failure = "Menu window state changed/unfocused"; }
                }
                if (!report.clockValid && report.failure == null) report.failure = "Unity clock differs from Stopwatch";
            }
            else report.failure = "Missing ready menu window";
        }
    }
}
