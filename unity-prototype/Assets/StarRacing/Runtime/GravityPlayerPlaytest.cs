using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StarRacingPrototype
{
    /// <summary>Opt-in native evidence run. Normal game startup never attaches this component.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GravityPlayerPlaytest : MonoBehaviour
    {
        [Serializable] sealed class JumpResult { public string theme, jump; public float flightSeconds, peak; public bool landed; }
        [Serializable] sealed class LoopResult { public string theme; public bool completed, fell; public int supportedTicks, ticks; }
        [Serializable] sealed class Report { public bool success; public string error; public float gravity = MagneticVehicle.GravityMagnitude; public List<JumpResult> jumps = new List<JumpResult>(); public List<LoopResult> loops = new List<LoopResult>(); public List<string> naturalRaces = new List<string>(); }
        string output; RaceDirector director; MagneticVehicle car; bool driving; double elapsed;
        readonly Report report = new Report();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-gravityPlaytest");
            if (at < 0 || at + 1 >= args.Length) return;
            var probe = new GameObject("Gravity native playtest").AddComponent<GravityPlayerPlaytest>();
            probe.output = Path.GetFullPath(args[at + 1]);
        }
        void FixedUpdate()
        {
            if (!driving || car == null) return;
            car.PrepareProjection(); elapsed += Time.fixedDeltaTime;
            car.SetRaceContext(true, 1, 2, elapsed);
        }
        static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            var run = Play();
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { Finish(e.Message); yield break; }
                if (!more) break;
                yield return run.Current;
            }
            Finish(null);
        }
        void Finish(string error)
        {
            report.success = error == null; report.error = error;
            File.WriteAllText(Path.Combine(output, "native-gravity.json"), JsonUtility.ToJson(report, true));
            Debug.Log((report.success ? "GRAVITY_PLAYER_OK " : "GRAVITY_PLAYER_FAILED ") + JsonUtility.ToJson(report));
            Application.Quit(report.success ? 0 : 1);
        }
        LocalRaceConfig Configuration(string theme) => new LocalRaceConfig
        { seed = "77", theme = theme, rails = "normal", jumps = true, entrants = 8, humans = 2, handicap = false };
        void Configure(string theme, bool isolated = true)
        {
            if (director.Started) { director.SetPaused(true); Require(director.ExitToMenu(), "Previous race exit failed"); }
            // The public transactional start path does not persist diagnostic preferences.
            Require(director.TryStartSelected(Configuration(theme), out var error), "Diagnostic start: " + error);
            car = director.Cars[0];
            director.GetComponent<RaceMenu>().enabled = !isolated;
            if (!isolated) return;
            foreach (var other in director.Cars) if (other != car) other.gameObject.SetActive(false);
            director.CameraFor(0).target = car; director.CameraFor(0).GetComponent<Camera>().rect = new Rect(0, 0, 1, 1);
            director.CameraFor(1).gameObject.SetActive(false);
        }
        void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
        IEnumerator Play()
        {
            yield return null;
            director = FindFirstObjectByType<RaceDirector>(); Require(director != null, "Missing real race scene");
            AudioListener.volume = 0;
            Capture("menu"); yield return new WaitForEndOfFrame();
            director.enabled = false; director.GetComponent<RaceHud>().enabled = false;
            Application.runInBackground = true;
            foreach (string theme in new[] { "cloud-city", "space-station" })
            {
                Configure(theme); Time.timeScale = 1;
                yield return null;
                foreach (var jump in director.Track.Route.Definition.jumps)
                {
                    driving = false; elapsed = 0;
                    car.ResetAt(30 + jump.launchIndex * 5 - 45, -(float)jump.lateralCenter); car.Hold(false);
                    car.Body.linearVelocity = car.Frame.tangent * 42;
                    car.SetInput(new DrivingInput { throttle = 1 }); director.CameraFor(0).Snap();
                    driving = true;
                    bool launched = false, landed = false, captured = false; float peak = 0; int flightTicks = 0, revision = car.PositionRevision;
                    for (int i = 0; i < 600; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        Require(Mathf.Abs(car.GravityAcceleration.magnitude - MagneticVehicle.GravityMagnitude) < .001f, "Variable gravity in native Player");
                        Require(car.AdditionalMagneticAdhesion, "Approved ground adhesion disabled");
                        if (car.ActiveJumpId != "") { launched = true; flightTicks++; }
                        if (launched)
                        {
                            peak = Mathf.Max(peak, Vector3.Dot(car.Body.position - car.Frame.position, car.Frame.normal));
                            if (!captured && Vector3.Dot(car.Body.linearVelocity, car.Frame.normal) <= 0)
                            { Capture(theme + "-" + jump.id + "-air"); captured = true; }
                        }
                        if (launched && car.ActiveJumpId == "" && car.HandlingSupportCount > 0 && car.Distance > 30 + jump.gapEndIndex * 5) { landed = true; break; }
                        if (car.IsFalling || car.PositionRevision != revision) break;
                    }
                    report.jumps.Add(new JumpResult { theme = theme, jump = jump.id, flightSeconds = flightTicks * Time.fixedDeltaTime, peak = peak, landed = landed });
                    Require(launched && landed, "Native ramp failed: " + theme + "/" + jump.id);
                    Capture(theme + "-" + jump.id + "-landing");
                    driving = false; car.Hold(true); yield return new WaitForEndOfFrame();
                }
                // Fall from an inverted part of the real track: no global-down fallback.
                float inverted = -1;
                foreach (var frame in director.Track.Route.Samples) if (Vector3.Dot(frame.normal, Vector3.up) < -.9f) { inverted = frame.distance; break; }
                Require(inverted >= 0, "No inverted road fixture");
                // Preserve a physically proven safe checkpoint on ordinary road. A
                // hand-picked inverted sample may overlap another rail and cannot admit one.
                car.ResetAt(80); car.Hold(false); car.SetInput(default); driving = true;
                for (int i = 0; i < 200; i++) yield return new WaitForFixedUpdate();
                Require(car.HasProvenCheckpoint, "Native fall fixture has no proven safe checkpoint");
                var invertedFrame = director.Track.Route.Evaluate(inverted);
                car.Distance = inverted; car.Frame = invertedFrame;
                car.Body.position = invertedFrame.position + invertedFrame.normal * 4; car.Body.rotation = invertedFrame.Rotation;
                car.transform.SetPositionAndRotation(car.Body.position, car.Body.rotation); Physics.SyncTransforms();
                yield return new WaitForFixedUpdate(); var remembered = car.GravityAcceleration;
                Require(Vector3.Dot(remembered, Vector3.up) > 8, "Inverted road direction");
                car.Body.position += car.Frame.right * (car.Frame.halfWidth + 30); car.Body.linearVelocity = Vector3.zero;
                int fallRevision = car.PositionRevision;
                for (int i = 0; i < 50; i++)
                {
                    yield return new WaitForFixedUpdate();
                    Require(Vector3.Distance(car.GravityAcceleration, remembered) < .05f, "Native off-road gravity changed direction");
                    Require(car.PositionRevision == fallRevision, "Unexpected early recovery");
                }
                Require(car.IsFalling, "Native fall not detected"); Capture(theme + "-frozen-fall");
                yield return new WaitForEndOfFrame();
                for (int i = 0; i < 200 && car.PositionRevision == fallRevision; i++) yield return new WaitForFixedUpdate();
                Require(car.PositionRevision > fallRevision && !car.IsFalling, "Native checkpoint recovery failed");
                Require(Vector3.Distance(car.GravityAcceleration, -car.Frame.normal * MagneticVehicle.GravityMagnitude) < .1f, "Checkpoint gravity not initialized");
                driving = false; car.Hold(true);
                Debug.Log("GRAVITY_PLAYER_THEME_OK theme=" + theme + " naturalJumps frozenInvertedFall checkpoint");
            }
            // Actual race lifecycle: autonomous AI must reach a natural finish; no
            // injected Results or artificial progress. Human/device acceptance stays separate.
            foreach (string theme in new[] { "cloud-city", "space-station" })
            {
                Configure(theme, false);
                director.enabled = true; director.GetComponent<RaceHud>().enabled = true;
                Require(director.Session.Phase == RacePhase.Countdown, "Native countdown missing");
                Capture(theme + "-countdown");
                yield return new WaitForSecondsRealtime(.2f);
                director.SetPaused(true); double before = director.Session.Elapsed;
                yield return new WaitForSecondsRealtime(.2f);
                Require(director.Session.Elapsed == before, "Pause advanced race"); director.SetPaused(false);
                var loop = new LoopResult { theme = theme };
                float deadline = Time.realtimeSinceStartup + 320;
                while (director.Session.Phase != RacePhase.Results && Time.realtimeSinceStartup < deadline)
                {
                    // The opt-in autonomous diagnostic does not depend on window focus.
                    if (director.Paused) director.SetPaused(false);
                    for (int i = 2; i < director.Cars.Length; i++)
                    {
                        var racer = director.Cars[i];
                        if (racer.Distance >= director.Track.Route.LoopStart && racer.Distance <= director.Track.Route.LoopEnd)
                        { loop.ticks++; if (racer.HandlingSupportCount > 0) loop.supportedTicks++; loop.fell |= racer.IsFalling; }
                        loop.completed |= director.Session.Racers[i].Progress > director.Track.Route.LoopEnd - director.Track.Route.StartDistance;
                    }
                    yield return new WaitForSecondsRealtime(.25f);
                }
                Require(director.Session.Phase == RacePhase.Results, "AI did not naturally finish: " + theme);
                bool aiFinished = false;
                for (int i = 2; i < director.Cars.Length; i++) aiFinished |= director.Session.Racers[i].Finished;
                Require(aiFinished, "Results without natural AI finish");
                report.naturalRaces.Add(theme); report.loops.Add(loop);
                Debug.Log("GRAVITY_PLAYER_LOOP " + JsonUtility.ToJson(loop));
                Capture(theme + "-results"); yield return new WaitForEndOfFrame();
                Require(director.ExitToMenu(), "Results did not return to menu");
                Capture(theme + "-return-menu"); yield return new WaitForEndOfFrame();
                Require(director.TryStartSelected(Configuration(theme), out var repeatError), "Repeat start: " + repeatError);
                Require(director.Session.Phase == RacePhase.Countdown, "Repeat countdown missing");
                director.SetPaused(true); Require(director.ExitToMenu(), "Repeat pause did not return to menu");
                Debug.Log("GRAVITY_PLAYER_LIFECYCLE_OK theme=" + theme + " countdown pause naturalAiFinish Results menu repeat");
                director.enabled = false; director.GetComponent<RaceHud>().enabled = false;
            }
            yield return null;
        }
    }
}
