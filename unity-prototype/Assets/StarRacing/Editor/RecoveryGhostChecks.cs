using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StarRacingPrototype {
    public static class RecoveryGhostChecks {
        static int assertions;
        public static int AssertionCount => assertions;
        static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        static void Require(bool condition, string label) {
            assertions++;
            if (!condition) throw new Exception("RECOVERY_GHOST " + label);
        }

        static void Invoke(MagneticVehicle vehicle, string method) =>
            typeof(MagneticVehicle).GetMethod(method, InstancePrivate).Invoke(vehicle, null);

        static void Ghost(MagneticVehicle vehicle) {
            vehicle.Recovery.Observe(.01f, true, 0, false, false, true, 0, 0, 0);
            vehicle.Recovery.Respawn();
        }

        sealed class PairFixture : IDisposable {
            public readonly List<GameObject> objects = new List<GameObject>();
            public readonly MagneticVehicle A, B, C;
            public PairFixture() {
                A = Create("ghost A"); B = Create("ghost B"); C = Create("ghost C");
            }

            MagneticVehicle Create(string name) {
                var gameObject = new GameObject(name);
                objects.Add(gameObject);
                gameObject.AddComponent<Rigidbody>();
                gameObject.AddComponent<BoxCollider>();
                var vehicle = gameObject.AddComponent<MagneticVehicle>();
                typeof(MagneticVehicle).GetField("chassis", InstancePrivate).SetValue(vehicle,
                    gameObject.GetComponent<BoxCollider>());
                // Use the production registration path, including compound CCD faces.
                Invoke(vehicle, "EnsurePhysics");
                return vehicle;
            }

            public Collider Collider(MagneticVehicle vehicle) => vehicle.GetComponent<Collider>();
            public bool Ignored(MagneticVehicle x, MagneticVehicle y) =>
                Physics.GetIgnoreCollision(Collider(x), Collider(y));
            public void Dispose() {
                foreach (GameObject gameObject in objects) if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        static void StaggeredExpiry(bool expireAFirst) {
            using (var f = new PairFixture()) {
                Ghost(f.A); Ghost(f.B);
                Invoke(f.A, "BeginGhosting");
                Invoke(f.B, "BeginGhosting");
                Require(f.Ignored(f.A, f.B), "both ghosting ignores shared pair");
                Require(f.Ignored(f.A, f.C), "first owner ignores third car");
                Require(f.Ignored(f.B, f.C), "second owner ignores third car");
                MagneticVehicle first = expireAFirst ? f.A : f.B;
                MagneticVehicle second = expireAFirst ? f.B : f.A;
                for (int i = 0; i < 200 && first.IsGhosting; i++) first.Recovery.StepGhost(.01f);
                Require(!first.IsGhosting && second.IsGhosting, "first timer expires independently");
                Invoke(first, "RestoreCarCollisions");
                Require(f.Ignored(f.A, f.B), "shared pair remains ignored while peer ghosts");
                Require(!f.Ignored(first, f.C), "expired owner releases non-ghost peer");
                for (int i = 0; i < 200 && second.IsGhosting; i++) second.Recovery.StepGhost(.01f);
                Require(!second.IsGhosting, "second timer expires");
                Invoke(second, "RestoreCarCollisions");
                Require(!f.Ignored(f.A, f.B), "last owner expiry releases shared pair");
                Require(!f.Ignored(f.A, f.C) && !f.Ignored(f.B, f.C), "all third-car pairs released");
            }
        }

        public static void Run() {
            assertions = 0;
            StaggeredExpiry(true);
            StaggeredExpiry(false);
            using (var f = new PairFixture()) {
                Ghost(f.A); Ghost(f.B);
                Invoke(f.A, "BeginGhosting"); Invoke(f.B, "BeginGhosting");
                f.A.Recovery.Reset();
                Invoke(f.A, "RestoreCarCollisions");
                Require(!f.A.IsGhosting && f.B.IsGhosting, "reset clears only its owner's protection");
                Require(f.Ignored(f.A, f.B), "reset preserves peer protection on shared pair");
                for (int i = 0; i < 200 && f.B.IsGhosting; i++) f.B.Recovery.StepGhost(.01f);
                Invoke(f.B, "RestoreCarCollisions");
                Require(!f.Ignored(f.A, f.B), "peer expiry after reset releases pair");
            }
            using (var f = new PairFixture()) {
                typeof(MagneticVehicle).GetProperty("FinishedCoasting").SetValue(f.A, true);
                Require(f.A.IsGhosting, "finish owner remains ghosting");
                Ghost(f.B); Invoke(f.B, "BeginGhosting");
                Invoke(f.A, "BeginGhosting");
                Require(f.Ignored(f.A, f.B), "finish owner ignores peer pair");
                for (int i = 0; i < 200 && f.B.IsGhosting; i++) f.B.Recovery.StepGhost(.01f);
                Invoke(f.B, "RestoreCarCollisions");
                Require(f.Ignored(f.A, f.B), "finished owner protects pair after peer expiry");
                typeof(MagneticVehicle).GetProperty("FinishedCoasting").SetValue(f.A, false);
                Invoke(f.A, "RestoreCarCollisions");
                Require(!f.Ignored(f.A, f.B), "finish owner cleanup releases final pair");
            }
            using (var f = new PairFixture()) {
                f.C.gameObject.SetActive(false);
                Ghost(f.A);Invoke(f.A,"BeginGhosting");
                Require(f.Ignored(f.A,f.B),"active registered peer is protected");
                Require(!f.Ignored(f.A,f.C),"inactive registered peer is excluded");
                f.C.gameObject.SetActive(true);Invoke(f.A,"BeginGhosting");
                Require(f.Ignored(f.A,f.C),"newly active peer joins protection");
            }
            MassRecovery();
            Debug.Log("RECOVERY_GHOST_CHECKS_OK assertions=" + assertions);
        }

        static int IgnoredCount(MagneticVehicle car)=>(typeof(MagneticVehicle).GetField("ignoredCars",InstancePrivate).GetValue(car) as List<Collider>).Count;
        static void MassRecovery() {
            var objects=new List<GameObject>();var cars=new List<MagneticVehicle>();
            try {
                for(int i=0;i<32;i++) {
                    var obj=new GameObject("mass recovery "+i);objects.Add(obj);
                    var car=obj.AddComponent<MagneticVehicle>();Invoke(car,"EnsurePhysics");cars.Add(car);
                }
                foreach(var car in cars) {
                    Require(car.Recovery.Observe(.01f,true,0,false,false,true,0,0,0),"mass fall starts");
                    Invoke(car,"BeginGhosting");
                    Require(IgnoredCount(car)==31,"fall owns exactly one entry per peer");
                    car.Recovery.Respawn();Invoke(car,"BeginGhosting");Invoke(car,"BeginGhosting");
                    Require(IgnoredCount(car)==31,"respawn and repeated protection retain unique pairs");
                }
                for(int i=0;i<cars.Count;i++) {
                    cars[i].Recovery.Reset();Invoke(cars[i],"RestoreCarCollisions");
                    Require(IgnoredCount(cars[i])==0,"expired owner releases its inventory");
                    for(int j=i+1;j<cars.Count;j++)Require(Physics.GetIgnoreCollision(cars[i].GetComponent<Collider>(),cars[j].GetComponent<Collider>()),"unexpired peer retains protection");
                }
                for(int i=0;i<cars.Count;i++)for(int j=i+1;j<cars.Count;j++)
                    Require(!Physics.GetIgnoreCollision(cars[i].GetComponent<Collider>(),cars[j].GetComponent<Collider>()),"last expiry restores mass recovery pairs");
            } finally {
                foreach(var obj in objects)if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            }
        }

    }
}
