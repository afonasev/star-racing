using System;
using System.Diagnostics;
using UnityEngine;

namespace StarRacingPrototype
{
    // Opt-in main-thread accounting. No callbacks or simulation decisions.
    public static class RaceCpuDiagnostics
    {
        public enum Section { Director, Vehicle, Stepper, Setup, Refine, Snapshot, Pairs, Forces, Native, Presentation, Cleanup }
        [Serializable] public struct Frame
        {
            public long director, vehicle, stepper, setup, refine, snapshot, pairs, forces, native, presentation, cleanup;
            public long nativeCalls, maxNativeCall, maxStepper, maxCallsPerTick;
            public int directorCalls, vehicleCalls, completedTicks;
            public int baseSlices, angularSlices, pairSlices, strongestSeat, strongestOtherSeat;
            public float strongestRefinement;
        }
        static Frame frame;
        public static bool Enabled { get; private set; }
        public static long Frequency => Stopwatch.Frequency;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Enabled = false; frame = default; }
        public static void Start()
        {
            if (Enabled) throw new InvalidOperationException("CPU diagnostics already active");
            frame = default; Enabled = true;
        }
        public static void Stop() { Enabled = false; frame = default; }
        public static Frame Drain() { var result = frame; frame = default; return result; }
        public static Scope Measure(Section section) => new Scope(section);
        // Observe the already computed winner; never query stateful vehicle limits again.
        public static void AdaptiveSlice(float remaining, float limit, int seat, int otherSeat, bool pair)
        {
            if (!Enabled) return;
            if (limit >= remaining) { frame.baseSlices++; return; }
            if (pair) frame.pairSlices++; else frame.angularSlices++;
            float ratio = remaining / limit;
            if (ratio > frame.strongestRefinement) {
                frame.strongestRefinement = ratio; frame.strongestSeat = seat; frame.strongestOtherSeat = otherSeat;
            }
        }
        public readonly struct Scope : IDisposable
        {
            readonly Section section;
            readonly long began, calls;
            readonly bool active;
            public Scope(Section value)
            {
                section = value; active = Enabled;
                calls = active && value == Section.Stepper ? frame.nativeCalls : 0;
                began = active ? Stopwatch.GetTimestamp() : 0;
            }
            public void Dispose()
            {
                if (!active || !Enabled) return;
                long elapsed = Stopwatch.GetTimestamp() - began;
                switch (section)
                {
                    case Section.Director: frame.director += elapsed; frame.directorCalls++; break;
                    case Section.Vehicle: frame.vehicle += elapsed; frame.vehicleCalls++; break;
                    case Section.Stepper:
                        frame.stepper += elapsed; frame.completedTicks++;
                        frame.maxStepper = Math.Max(frame.maxStepper, elapsed);
                        frame.maxCallsPerTick = Math.Max(frame.maxCallsPerTick, frame.nativeCalls - calls); break;
                    case Section.Setup: frame.setup += elapsed; break;
                    case Section.Refine: frame.refine += elapsed; break;
                    case Section.Snapshot: frame.snapshot += elapsed; break;
                    case Section.Pairs: frame.pairs += elapsed; break;
                    case Section.Forces: frame.forces += elapsed; break;
                    case Section.Native:
                        frame.native += elapsed; frame.nativeCalls++;
                        frame.maxNativeCall = Math.Max(frame.maxNativeCall, elapsed); break;
                    case Section.Presentation: frame.presentation += elapsed; break;
                    case Section.Cleanup: frame.cleanup += elapsed; break;
                }
            }
        }
    }
}
