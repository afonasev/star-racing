using System;
using System.IO;
using UnityEngine;

namespace StarRacingPrototype
{
    public static class RaceClockDiagnosticsChecks
    {
        static void Check(bool valid, string message) { if (!valid) throw new Exception("Menu clock: " + message); }
        public static void Run()
        {
            string path = Environment.GetEnvironmentVariable("STAR_RACING_MENU_CLOCK_REPORT");
            string json = File.ReadAllText(path);
            var report = JsonUtility.FromJson<RaceClockDiagnostics.Report>(json);
            RaceClockDiagnostics.ValidateWindow(report);
            Check(report.clockValid && report.samples[report.readyAt].scale == 0, "recorded focused main menu with paused simulation");
            Check(report.wallSeconds >= 10, "recorded window shorter than ten seconds");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "menu-clock-revalidated.json"), JsonUtility.ToJson(report, true));
            var bad = JsonUtility.FromJson<RaceClockDiagnostics.Report>(json);
            var last = bad.samples[bad.samples.Count - 1]; last.unscaledTime += 5; bad.samples[bad.samples.Count - 1] = last;
            RaceClockDiagnostics.ValidateWindow(bad); Check(!bad.clockValid && bad.failure == "Unity clock differs from Stopwatch", "clock drift was accepted");
            bad = JsonUtility.FromJson<RaceClockDiagnostics.Report>(json); last = bad.samples[bad.samples.Count - 1]; last.scale = .5f; bad.samples[bad.samples.Count - 1] = last;
            RaceClockDiagnostics.ValidateWindow(bad); Check(!bad.clockValid && bad.failure == "Menu window state changed/unfocused", "scale change was accepted");
            RaceClockDiagnostics.ValidateWindow(new RaceClockDiagnostics.Report());
            Debug.Log("MENU_CLOCK_CHECKS_OK recordedZeroScale=true driftRejected=true stateChangeRejected=true missingWindowSafe=true");
        }
    }
}
