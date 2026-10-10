using System;

namespace StarRacingPrototype {
    public enum RacePhase { Ready, Countdown, Racing, FinishWindow, Results }

    public struct RaceObservation {
        public float distance;
        public int revision;
        public bool valid;
        public RaceObservation(float distance, int revision = 0, bool valid = true) {
            this.distance = distance; this.revision = revision; this.valid = valid;
        }
    }

    /// <summary>Race rules only. No transforms, input devices, rendering or wall-clock dependency.</summary>
    public sealed class RaceSession {
        public sealed class Racer {
            public float Progress { get; internal set; }
            public double FinishTime { get; internal set; } = -1;
            public bool Finished => FinishTime >= 0;
            public int Place { get; internal set; } = 1;
            internal float previous, nextGate;
            internal int revision;
            internal bool valid;
            internal double gateTime;
        }
        public readonly Racer[] Racers;
        readonly double[] candidates;
        readonly RaceObservation[] pair = new RaceObservation[2];
        public RacePhase Phase { get; private set; }
        public bool Paused { get; set; }
        public double Elapsed { get; private set; }
        public double Countdown { get; private set; } = 3;
        public double Remaining => resultsAt < 0 ? 3 : Math.Max(0, resultsAt - Elapsed);
        public float RaceLength => finish - start;
        readonly float start, finish;
        readonly int humanCount;
        double resultsAt = -1;
        int humanFinishCount;
        int finishCount;

        public RaceSession(float start, float finish, int count=2, int humans=-1) {
            if(count<1||count>64)throw new ArgumentOutOfRangeException(nameof(count));
            humanCount = humans < 0 ? count : humans;
            if (humanCount < 1 || humanCount > count) throw new ArgumentOutOfRangeException(nameof(humans));
            Racers=new Racer[count];candidates=new double[count];
            if (finish <= start) throw new ArgumentException("Finish must follow start");
            this.start = start; this.finish = finish;
            var initial=new RaceObservation[count];for(int i=0;i<count;i++)initial[i]=new RaceObservation(start);Reset(initial);
        }
        public void Reset(RaceObservation a, RaceObservation b) {pair[0]=a;pair[1]=b;Reset(pair);}
        public void Reset(RaceObservation[] observations) {
            if(observations.Length!=Racers.Length)throw new ArgumentException("Roster observation count");
            Phase = RacePhase.Ready; Paused = false; Elapsed = 0; Countdown = 3;
            resultsAt = -1; humanFinishCount = 0; finishCount = 0;
            for (int i = 0; i < Racers.Length; i++) {
                var o = observations[i];
                Racers[i] = new Racer { previous = o.distance, revision = o.revision, valid = o.valid, Progress=Math.Max(0,Math.Min(o.distance-start,RaceLength)), nextGate = Math.Min(start + (float)(Math.Floor(Math.Max(0,o.distance-start)/50)+1)*50, finish) };
            }
        }
        public void Begin() { if (Phase == RacePhase.Ready) Phase = RacePhase.Countdown; }
        public bool CanDrive(int seat) => !Paused && !Racers[seat].Finished && (Phase == RacePhase.Racing || Phase == RacePhase.FinishWindow);
        public void Tick(double dt, RaceObservation a, RaceObservation b) {pair[0]=a;pair[1]=b;Tick(dt,pair);}
        public void Tick(double dt, RaceObservation[] observations) {
            if(observations.Length!=Racers.Length)throw new ArgumentException("Roster observation count");
            if (Paused || dt <= 0 || Phase == RacePhase.Ready || Phase == RacePhase.Results) return;
            if (Phase == RacePhase.Countdown) {
                Countdown = Math.Max(0, Countdown - dt);
                if (Countdown < .000001) { Countdown = 0; Phase = RacePhase.Racing; }
                return;
            }
            double end = Elapsed + dt;
            if (resultsAt >= 0) end = Math.Min(end, resultsAt);
            double step = end - Elapsed;
            for(int i=0;i<Racers.Length;i++)candidates[i]=Observe(i,observations[i],step,dt);
            for(int j=0;j<Racers.Length;j++) {
                int next=-1;double earliest=double.PositiveInfinity;
                for(int i=0;i<candidates.Length;i++)if(candidates[i]>=0 && candidates[i]<earliest){next=i;earliest=candidates[i];}
                if(next<0)break;Finish(next,earliest);candidates[next]=-1;
            }
            Elapsed = resultsAt >= 0 ? Math.Min(end, resultsAt) : end;
            Rank();
            if (resultsAt >= 0 && Elapsed >= resultsAt - .000001) Phase = RacePhase.Results;
        }
        double Observe(int seat, RaceObservation o, double step, double dt) {
            var r = Racers[seat];
            if (r.Finished) return -1;
            float before = r.previous;
            float after = before + (o.distance - before) * (float)(step / dt);
            bool continuous = r.valid && o.valid && r.revision == o.revision && Math.Abs(o.distance - before) <= 30;
            r.previous = o.distance; r.revision = o.revision; r.valid = o.valid;
            // Recovery may reduce progress, but never advances the validated checkpoint frontier.
            r.Progress = Math.Max(0, Math.Min(Math.Min(after, r.nextGate) - start, RaceLength));
            if (!continuous || after <= before) return -1;
            // A short invalid/recovery observation can straddle an intermediate checkpoint.
            // Accept it only from a subsequent continuous on-road step still near that gate;
            // a large teleport cannot advance the frontier or produce a finish.
            if (r.nextGate < finish && before >= r.nextGate && after-r.nextGate <= 30)
                before = r.nextGate-.001f;
            while (r.nextGate < finish && before <= r.nextGate && after >= r.nextGate) {
                r.gateTime = Elapsed + step * (r.nextGate - before) / (after - before);
                r.nextGate = Math.Min(r.nextGate + 50, finish);
            }
            r.Progress = Math.Max(0, Math.Min(Math.Min(after, r.nextGate) - start, RaceLength));
            if (r.nextGate == finish && before < finish && after >= finish) return Elapsed + step * (finish - before) / (after - before);
            return -1;
        }
        void Finish(int seat, double time) {
            if (time < 0 || (resultsAt >= 0 && time > resultsAt)) return;
            var r = Racers[seat]; r.FinishTime = time; r.Place = ++finishCount; r.Progress = RaceLength;
            // Roster stores local humans first, independently of their shuffled grid slots.
            if (seat < humanCount && ++humanFinishCount == humanCount) {
                resultsAt = time + 3; Phase = RacePhase.FinishWindow;
            }
        }
        void Rank() {
            for (int i = 0; i < Racers.Length; i++) {
                var r = Racers[i]; if (r.Finished) continue;
                r.Place=1;
                for(int j=0;j<Racers.Length;j++) {
                    if(j==i)continue;var other=Racers[j];
                    if(other.Finished || other.Progress>r.Progress+.01f ||
                       (Math.Abs(other.Progress-r.Progress)<=.01f && (other.gateTime<r.gateTime || (other.gateTime==r.gateTime && j<i))))r.Place++;
                }
            }
        }
    }
}
