using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace StarRacingPrototype
{
    // Opt-in evidence runner; inert without its command-line flag. Uses production input and race rules.
    public sealed class VehicleExhaustPlaytest : MonoBehaviour
    {
        string output,previous;bool hadPrevious,saved;Gamepad pad;RaceDirector director;int assertions;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"--vehicle-proof-dir");if(i>=0&&i+1<a.Length)new GameObject("C05 native proof").AddComponent<VehicleExhaustPlaytest>().output=Path.GetFullPath(a[i+1]);}
        IEnumerator Start()
        {
            Application.runInBackground=true;AudioListener.volume=0;Directory.CreateDirectory(output);
            hadPrevious=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);previous=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);saved=true;
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){object next=null;bool moved=false;Exception error=null;try{var r=stack.Peek();moved=r.MoveNext();if(moved)next=r.Current;else stack.Pop();}catch(Exception e){error=e;}
                if(error!=null){File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Debug.LogException(error);Restore();Application.Quit(1);yield break;}
                if(!moved)continue;if(next is IEnumerator nested){stack.Push(nested);continue;}yield return next;
            }
            File.WriteAllText(Path.Combine(output,"success.json"),"{\"success\":true,\"assertions\":"+assertions+",\"physical_devices\":false,\"natural_ai_finish\":true}");
            Debug.Log("C05_PLAYER_OK assertions="+assertions);Restore();Application.Quit();
        }
        void Check(bool condition,string name){assertions++;if(!condition)throw new Exception("C05_PLAYER "+name);Debug.Log("C05_PLAYER_CHECK "+name);}
        void Input(float gas,bool nitro=false){var state=new GamepadState{rightTrigger=gas};if(nitro)state=state.WithButton(GamepadButton.East);InputSystem.QueueStateEvent(pad,state);}
        IEnumerator Shot(string name,bool detail=false)
        {
            var chase=director.CameraFor(0);var camera=chase.GetComponent<Camera>();Vector3 position=camera.transform.position;Quaternion rotation=camera.transform.rotation;
            if(detail){chase.enabled=false;var car=director.Cars[0].transform;camera.transform.position=car.TransformPoint(new Vector3(5,3,-7));camera.transform.LookAt(car.TransformPoint(new Vector3(0,.4f,-.3f)));}
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.2f);
            if(detail){camera.transform.SetPositionAndRotation(position,rotation);chase.enabled=true;}
        }
        void Begin(string theme,int count)
        {
            var config=LocalRaceConfig.Load();config.humans=1;config.entrants=count;config.devices=new[]{SessionControllers.Slot(pad),-1,0,1};config.theme=theme;config.seed="77";config.rails="normal";config.jumps=false;
            Check(director.TryStartSelected(config,out string error),"start "+theme+" "+count+" "+error);
        }
        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1);director=FindAnyObjectByType<RaceDirector>();pad=InputSystem.AddDevice<Gamepad>();
            Begin("cloud-city",8);var car=director.Cars[0];var exhaust=car.GetComponentInChildren<VehicleExhaust>();
            Vector3 start=car.Body.position;float charge=car.Drive.Charge;Input(.25f);yield return new WaitForSeconds(.25f);float partial=exhaust.Envelope.Length;
            Input(1,true);yield return new WaitForSeconds(.75f);
            Check(director.Session.Phase==RacePhase.Countdown&&car.Body.isKinematic,"countdown locked");
            Check((car.Body.position-start).sqrMagnitude<.000001f&&car.Body.linearVelocity.sqrMagnitude<.000001f,"countdown no movement");
            Check(partial>0&&exhaust.Envelope.Length>partial,"countdown analog strength and buildup");
            Check(!car.Drive.Active&&car.Drive.Charge==charge&&exhaust.Envelope.NitroLength==0,"countdown no nitro use");
            yield return Shot("01-countdown-gas",true);Input(1);
            while(director.Session.Phase==RacePhase.Countdown)yield return null;
            yield return new WaitForSeconds(1);
            Check(!car.Body.isKinematic&&car.Body.linearVelocity.magnitude>1,"green unlocks normal movement");
            Input(1,true);float timeout=Time.time+3;while(!car.Drive.Active&&Time.time<timeout)yield return null;
            Check(car.Drive.Active,"real nitro active");yield return new WaitForSeconds(.4f);float shortNitro=exhaust.Envelope.Length;yield return new WaitForSeconds(.5f);
            Check(exhaust.Envelope.Length>shortNitro&&exhaust.Envelope.NitroBlend>.9f,"nitro grows while held");
            yield return Shot("02-nitro",true);Input(0);yield return new WaitForSeconds(.9f);Check(exhaust.Envelope.Length<.003f,"release extinguishes");
            director.SetPaused(true);yield return null;Check(!exhaust.Emitting,"pause clears effect");Input(1,true);director.StartRace();yield return new WaitForSeconds(.2f);Check(!exhaust.Emitting,"resume requires neutral");
            Input(0);yield return null;yield return null;Input(1);yield return new WaitForSeconds(.3f);Check(exhaust.Emitting,"neutral rearms input");
            int revision=car.PositionRevision;car.ResetToCheckpoint();yield return null;Check(car.PositionRevision>revision&&exhaust.Envelope.GasHold<.2f,"recovery resets hold");
            director.Restart();yield return null;Check(exhaust.Envelope.NitroLength==0,"repeat clears nitro");
            director.SetPaused(true);Check(director.ExitToMenu(),"return menu");Input(0);
            Begin("space-station",64);yield return Shot("03-space-64-colors",true);
            int bodies=0;foreach(var c in director.Cars){var v=c.GetComponentInChildren<StaticVehicleVisual>();Check(v!=null,"C05 entrant "+bodies);bodies++;}Check(bodies==64,"64 model entrants");
            while(director.Session.Phase==RacePhase.Countdown)yield return null;
            var times=new List<float>();float until=Time.realtimeSinceStartup+5;
            while(Time.realtimeSinceStartup<until){times.Add(Time.unscaledDeltaTime*1000);yield return null;}
            times.Sort();File.WriteAllText(Path.Combine(output,"frame-times-64.json"),"{\"samples\":"+times.Count+",\"median_ms\":"+times[times.Count/2].ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"p95_ms\":"+times[Mathf.Min(times.Count-1,(int)(times.Count*.95f))].ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"camera_count\":1,\"physical_acceptance\":false}");
            director.SetPaused(true);Check(director.ExitToMenu(),"64 return menu");
            Begin("cloud-city",8);Input(0);float deadline=Time.realtimeSinceStartup+600;
            while(director.Session.Phase!=RacePhase.Results&&Time.realtimeSinceStartup<deadline){if(director.Paused&&!director.Input.MissingDevice)director.StartRace();yield return null;}
            Check(director.Session.Phase==RacePhase.Results,"natural results");bool finished=false;for(int i=1;i<director.Cars.Length;i++)finished|=director.Session.Racers[i].Finished;Check(finished,"natural AI finish");
            yield return Shot("04-natural-results");Check(director.ExitToMenu(),"results menu");
        }
        void OnApplicationQuit()=>Restore();
        void OnDestroy()=>Restore();
        void Restore(){if(!saved)return;saved=false;if(hadPrevious)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,previous);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);}
    }
}
