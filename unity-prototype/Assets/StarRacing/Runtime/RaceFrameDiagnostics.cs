using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StarRacingPrototype
{
    /// <summary>Opt-in frame telemetry; never supplies input or changes simulation/graphics settings.</summary>
    public sealed class RaceFrameDiagnostics : MonoBehaviour
    {
        public const string Argument = "--race-frame-diagnostics-dir";
        [Serializable] public sealed class Frame
        {
            public int unityFrame, phase;
            public bool paused, focused, steady, timingAvailable, gpuAvailable, nativeDeltaAvailable;
            public double realSeconds, deltaMs, unscaledDeltaMs, simulatedSeconds, simulationDeltaSeconds;
            public float timeScale, maximumDeltaTime, captureDeltaTime;
            public long nativeSteps, nativeDelta;
            public RaceCpuDiagnostics.Frame cpu;
            public int gc0, gc1, gc2;
            public ulong timingTimestamp;
            public double cpuMs, cpuMainMs, cpuPresentWaitMs, cpuRenderMs, gpuMs;
        }
        [Serializable] public sealed class Report
        {
            public string version, platform, gpu, appliedConfig;
            public string limitation = "Observed real-time frames. FrameTiming CPU/GPU values are delayed by four frames; native steps are observed at the current Update and are not aligned to delayed GPU frames. Zero/missing GPU data is unavailable, not zero GPU cost. Instrumentation has overhead; no human acceptance.";
            public int width, height, entrants, baseSubsteps;
            public bool cpuEnabled, hitchSearch, severeFrameObserved;
            public long stopwatchFrequency;
            public int observedFrames, droppedFrames;
            public float fixedDeltaTime, captureDeltaTime;
            public bool featureEnabled, completed, editor, gpuAvailable;
            public string failure;
            public double measuredSimulatedSeconds;
            public List<Frame> frames = new List<Frame>(4096);
        }
        RaceDirector director;
        readonly FrameTiming[] timings = new FrameTiming[1];
        Report report;
        string output;
        double deadline, raceBegin = -1, previousSim, previousReal;
        long previousSteps;
        ulong previousTimestamp;
        bool hasTimestamp, hasPrevious, began;
        double observedBegan = -1, observedSim, raceRealBegin = -1;
        int gc0, gc1, gc2;
        const int CpuFrameBound = 8192;
        int ringAt;
        double matchedSimulationSeconds;
        public bool Completed { get; private set; }
        public string Failure => report?.failure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, Argument);
            if (at < 0) return;
            if (at + 1 >= args.Length) throw new ArgumentException("Frame diagnostic output required");
            new GameObject("Opt-in frame diagnostics").AddComponent<RaceFrameDiagnostics>().Begin(args[at + 1], null, Array.IndexOf(args, "--race-cpu-diagnostics") >= 0, Array.IndexOf(args, "--race-cpu-diagnostics") >= 0);
        }

        public void Begin(string directory, RaceDirector source = null, bool cpu = false, bool hitchSearch = false, double matchSimulationSeconds = 0)
        {
            if (began) throw new InvalidOperationException("Frame diagnostics already started");
            if (!SilentStartup.Enabled) throw new InvalidOperationException("Silent startup required");
            if (!Path.IsPathRooted(directory)) throw new ArgumentException("Absolute output required");
            if (Time.captureDeltaTime != 0) throw new InvalidOperationException("Real-time frame capture requires captureDeltaTime=0");
            if (RacePhysicsStepper.Substeps != 8 || Mathf.Abs(Time.fixedDeltaTime - .01f) > 1e-8f)
                throw new InvalidOperationException("Expected unchanged 100 Hz / 8 base substeps");
            Directory.CreateDirectory(directory);
            if (File.Exists(Path.Combine(directory, "frames.json")) || File.Exists(Path.Combine(directory, "probe-started.json"))) throw new IOException("Fresh diagnostic output required");
            matchedSimulationSeconds = matchSimulationSeconds;
            output = directory; director = source; began = true; deadline = Time.realtimeSinceStartupAsDouble + 600;
            report = new Report { version = Application.version, platform = Application.platform.ToString(),
                gpu = SystemInfo.graphicsDeviceName, editor = Application.isEditor, width = Screen.width, height = Screen.height,
                fixedDeltaTime = Time.fixedDeltaTime, captureDeltaTime = Time.captureDeltaTime, baseSubsteps = RacePhysicsStepper.Substeps };
            report.cpuEnabled = cpu; report.hitchSearch = hitchSearch; report.stopwatchFrequency = RaceCpuDiagnostics.Frequency;
            gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
            if (cpu) RaceCpuDiagnostics.Start();
            // Do not activate frame timing dynamically: the first native diagnostic launch
            // advanced Unity time by only 10 microseconds per frame while wall time advanced normally.
            // Reuse timing packets only when the Player already has this feature enabled.
            File.WriteAllText(Path.Combine(output, "probe-started.json"), JsonUtility.ToJson(report, true));
        }

        void Update()
        {
            if (!began || Completed) return;
            try
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Frame diagnostic deadline");
                if (director == null) director = FindAnyObjectByType<RaceDirector>();
                double real = Time.realtimeSinceStartupAsDouble;
                double simulated = RacePhysicsStepper.SimulatedSeconds;
                long steps = RacePhysicsStepper.NativeSteps;
                var cpu = report.cpuEnabled ? RaceCpuDiagnostics.Drain() : default;
                int collections0 = GC.CollectionCount(0), collections1 = GC.CollectionCount(1), collections2 = GC.CollectionCount(2);
                int delta0 = collections0 - gc0, delta1 = collections1 - gc1, delta2 = collections2 - gc2;
                gc0 = collections0; gc1 = collections1; gc2 = collections2;
                if (director == null || !director.Started || director.Session == null)
                {
                    previousSteps = steps; previousSim = simulated; previousReal = real; hasPrevious = true;
                    return;
                }
                if (report.entrants == 0)
                {
                    report.entrants = director.Cars.Length;
                    if (report.entrants != 32) throw new InvalidOperationException("Exact cloud32 diagnostic requires 32 entrants");
                    report.appliedConfig = JsonUtility.ToJson(director.AppliedConfig);
                }
                if (observedBegan < 0) { observedBegan = real; observedSim = simulated; }
                if (raceBegin < 0 && director.Session.Phase == RacePhase.Racing) { raceBegin = simulated; raceRealBegin = real; }
                var frame = new Frame { unityFrame = Time.frameCount, phase = (int)director.Session.Phase,
                    cpu = cpu, gc0 = delta0, gc1 = delta1, gc2 = delta2,
                    paused = director.Paused, focused = Application.isFocused, realSeconds = real,
                    timeScale = Time.timeScale, maximumDeltaTime = Time.maximumDeltaTime, captureDeltaTime = Time.captureDeltaTime,
                    deltaMs = hasPrevious ? (real - previousReal) * 1000d : 0, unscaledDeltaMs = Time.unscaledDeltaTime * 1000d,
                    nativeSteps = steps, nativeDelta = hasPrevious ? steps - previousSteps : 0, nativeDeltaAvailable = hasPrevious,
                    simulatedSeconds = simulated, simulationDeltaSeconds = hasPrevious ? simulated - previousSim : 0,
                    steady = raceBegin >= 0 && simulated - raceBegin >= 2 };
                report.featureEnabled = FrameTimingManager.IsFeatureEnabled();
                if (report.featureEnabled) FrameTimingManager.CaptureFrameTimings();
                if (report.featureEnabled && FrameTimingManager.GetLatestTimings(1, timings) > 0 && (!hasTimestamp || timings[0].frameStartTimestamp > previousTimestamp))
                {
                    var timing = timings[0]; hasTimestamp = true; previousTimestamp = timing.frameStartTimestamp;
                    frame.timingAvailable = true; frame.timingTimestamp = timing.frameStartTimestamp;
                    frame.cpuMs = timing.cpuFrameTime; frame.cpuMainMs = timing.cpuMainThreadFrameTime;
                    frame.cpuPresentWaitMs = timing.cpuMainThreadPresentWaitTime; frame.cpuRenderMs = timing.cpuRenderThreadFrameTime;
                    frame.gpuMs = timing.gpuFrameTime; frame.gpuAvailable = timing.gpuFrameTime > 0;
                    report.gpuAvailable |= frame.gpuAvailable;
                }
                if (report.cpuEnabled && hasPrevious && frame.nativeDelta != cpu.nativeCalls)
                    throw new InvalidOperationException("CPU native-call accounting mismatch: " + cpu.nativeCalls + " != " + frame.nativeDelta);
                report.observedFrames++;
                report.severeFrameObserved |= frame.steady && !frame.paused && frame.focused && frame.deltaMs >= 80;
                if (!report.hitchSearch || report.frames.Count < CpuFrameBound) report.frames.Add(frame);
                else { report.frames[ringAt] = frame; ringAt = (ringAt + 1) % CpuFrameBound; report.droppedFrames++; }
                previousSteps = steps; previousSim = simulated; previousReal = real; hasPrevious = true;
                if (real - observedBegan > 30 && simulated - observedSim < 1)
                    throw new InvalidOperationException("Simulation did not advance one second in 30 observed wall seconds; inspect pause and clock fields");
                if (report.frames.Count > 50000) throw new InvalidOperationException("Frame diagnostic sample bound");
                if (raceBegin >= 0 && (matchedSimulationSeconds > 0 ? simulated - raceBegin >= matchedSimulationSeconds + 2 : report.hitchSearch ? real - raceRealBegin >= 60 : simulated - raceBegin >= 14))
                {
                    report.measuredSimulatedSeconds = simulated - raceBegin - 2;
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }

        void OnDestroy() { if (began && report != null && report.cpuEnabled && !Completed) RaceCpuDiagnostics.Stop(); }

        void Finish(Exception error)
        {
            report.completed = error == null; report.failure = error?.ToString(); Completed = true;
            if (report.cpuEnabled) RaceCpuDiagnostics.Stop();
            if (report.hitchSearch && report.droppedFrames > 0)
            {
                var ordered = new List<Frame>(CpuFrameBound);
                for (int i = 0; i < CpuFrameBound; i++) ordered.Add(report.frames[(ringAt + i) % CpuFrameBound]);
                report.frames = ordered;
            }
            File.WriteAllText(Path.Combine(output, "frames.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(error == null ? 0 : 1);
        }
    }
}
