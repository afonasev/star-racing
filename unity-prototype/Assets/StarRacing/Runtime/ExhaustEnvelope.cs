using System;
namespace StarRacingPrototype
{
    // Presentation only: never advances propulsion, boost resources or race time.
    public sealed class ExhaustEnvelope
    {
        float gasPulse,nitroPulse,pulseThrottle;
        public float GasSignal => GasLength;
        public float NitroSignal => NitroLength / 2.6f;
        public float GasHold { get; private set; }
        public float NitroHold { get; private set; }
        public float GasLength { get; private set; }
        public float NitroLength { get; private set; }
        public float Length => Math.Max(GasLength, NitroLength);
        public float NitroBlend => Length < .001f ? 0 : Math.Min(1, NitroLength / Math.Max(.001f, GasLength));
        public void Reset() { gasPulse = nitroPulse = pulseThrottle = GasHold = NitroHold = GasLength = NitroLength = 0; }
        public void Step(float throttle, bool boostActive, bool enabled, float dt)
        {
            if (!enabled) { Reset(); return; }
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            throttle = float.IsNaN(throttle) ? 0 : Math.Max(0, Math.Min(1, throttle));
            // A perceptible onset survives a one-frame tap; resource use remains in physics.
            if(throttle > .001f){gasPulse=.06f;pulseThrottle=throttle;}
            else if(gasPulse>0)throttle=pulseThrottle;
            if(boostActive)nitroPulse=.06f;
            else boostActive=nitroPulse>0;
            gasPulse=Math.Max(0,gasPulse-dt);nitroPulse=Math.Max(0,nitroPulse-dt);
            GasHold = throttle > .001f ? Math.Min(3, GasHold + dt) : 0;
            NitroHold = boostActive ? Math.Min(3, NitroHold + dt) : 0;
            float gas = throttle * (.35f + .65f * (1 - (float)Math.Exp(-GasHold / .75f)));
            float nitro = boostActive ? 1.4f + 1.2f * (1 - (float)Math.Exp(-NitroHold / .6f)) : 0;
            if(throttle > .001f)GasLength=Math.Max(GasLength,throttle*.35f);
            if(boostActive)NitroLength=Math.Max(NitroLength,1.4f);
            GasLength = Ease(GasLength, gas, dt, gas > GasLength ? .09f : .12f);
            NitroLength = Ease(NitroLength, nitro, dt, nitro > NitroLength ? .12f : .09f);
            if (gas == 0 && GasLength < .001f) GasLength = 0;
            if (nitro == 0 && NitroLength < .001f) NitroLength = 0;
        }
        static float Ease(float value, float target, float dt, float seconds) => target + (value - target) * (float)Math.Exp(-dt / seconds);
    }
}
