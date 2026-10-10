using System;

namespace StarRacingPrototype {
    public enum RecoveryEventKind { Fall, Respawn }

    public readonly struct RecoveryEvent {
        public readonly RecoveryEventKind Kind;
        public readonly int Entrant, Generation, Episode, RevisionBefore, RevisionAfter;
        public readonly long Tick, Sequence;
        public RecoveryEvent(RecoveryEventKind kind, int entrant, int generation, int episode,
            int before, int after, long tick, long sequence) {
            Kind=kind;Entrant=entrant;Generation=generation;Episode=episode;
            RevisionBefore=before;RevisionAfter=after;Tick=tick;Sequence=sequence;
        }
    }

    // Simulation-time source recovery rules. The Rigidbody remains the continuous pose owner.
    public sealed class RecoveryPolicy {
        public const float FallDelay=1.5f, GhostDuration=1.5f;
        public bool Falling {get;private set;}
        public float FallSeconds {get;private set;}
        public float StuckSeconds {get;private set;}
        public float GhostSeconds {get;private set;}
        public int Episode {get;private set;}
        public void Reset() {Falling=false;FallSeconds=StuckSeconds=GhostSeconds=0;Episode=0;}
        public void StepGhost(float dt) {if(dt>0)GhostSeconds=Math.Max(0,GhostSeconds-dt);}
        public bool Observe(float dt, bool racing, double raceElapsed, bool finished,
            bool confirmedJump, bool unsupportedOffRoad, float speed, float gas, float brake) {
            if(dt<=0||!racing||finished||Falling)return false;
            if(confirmedJump) {StuckSeconds=0;return false;}
            if(unsupportedOffRoad && Math.Abs(speed)<.4f && gas==0 && brake==0)StuckSeconds+=dt;
            else StuckSeconds=0;
            // Lack of input is not a fall: a parked car must retain its road support.
            // Only a physically unsupported off-road pose may enter fall recovery.
            if(!unsupportedOffRoad)return false;
            Falling=true;FallSeconds=0;StuckSeconds=0;Episode++;
            return true;
        }
        public bool AdvanceFall(float dt) {
            if(!Falling||dt<=0)return false;
            FallSeconds+=dt;
            return FallSeconds>=FallDelay;
        }
        public void Respawn() {
            if(!Falling)throw new InvalidOperationException("No recovery episode");
            Falling=false;FallSeconds=StuckSeconds=0;GhostSeconds=GhostDuration;
        }
    }
}
