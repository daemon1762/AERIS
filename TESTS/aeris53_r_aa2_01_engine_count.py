#!/usr/bin/env python3
"""Run production balancing/optimizer and update order at stubbed KSP boundaries.

No production DLL, game settings or live vessel is modified. Requires mcs/mono.
The fixture controls engine discovery and torque observations; it is not KSP.
"""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
AA = ROOT / "Source/AERISFlightControl/AA"


def method(path, signature):
    text = path.read_text(encoding="utf-8-sig")
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


HARNESS = r'''
using System;
using System.Collections.Generic;
using UnityEngine;
public class FlightCtrlState {}
public class ModuleEngines { public float thrustPercentage=100; }
public class Vessel {
    public List<int> Parts=new List<int>{1};
    public uint referenceTransformId=1;
    public bool LandedOrSplashed(){return true;}
}
namespace UnityEngine {
    public struct Vector3 {
        float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero {get{return new Vector3();}}
        public float this[int axis] {get{return axis==0?x:axis==1?y:z;}}
    }
    public enum KeyCode {None}
    public static class Input {public static bool GetKeyDown(KeyCode k){return false;}}
    public static class Time {public static float fixedTime;}
}
namespace AtmosphereAutopilot {
    public class AutoGuiAttr:Attribute {public AutoGuiAttr(string s,bool b,string f="") {}}
    public class VesselSerializable:Attribute {public VesselSerializable(string s) {}}
    public class GlobalSerializable:Attribute {public GlobalSerializable(string s) {}}
    public class AutoHotkeyAttr:Attribute {public AutoHotkeyAttr(string s) {}}
    public class AutopilotModule {public virtual void OnUpdate() {}}
    public static class MessageManager {public static void post_status_message(string s) {}}
    public class Buffer {public int Size;public float value;public float getLast(){return value;}}
    public sealed partial class FlightModel {
        const int PITCH=0,ROLL=1,YAW=2,FullMomentFreq=40;
        int moments_cycle_counter,prev_part_count;
        uint reference_transform_id=uint.MaxValue;
        bool sequential_dt,any_gimbals;
        float LastModelUpdateFixedTime;
        int ModelUpdateSequence;
        Vector3 reaction_torque,MOI=new Vector3(10,10,10);
        Buffer[] angular_acc_buf={new Buffer()},gimbal_buf={new Buffer(),new Buffer(),new Buffer()};
        Vessel vessel=new Vessel();
        public class EngineMoment {
            public ModuleEngines engine=new ModuleEngines();
            public Vector3 potential_torque,torque_components;
            public double estimated_max_thrust_unlimited=100;
            public EngineMoment(float torque){potential_torque=new Vector3(torque,0,0);torque_components=new Vector3(Math.Sign(torque),0,0);}
        }
        List<EngineMoment> engines=new List<EngineMoment>(),observed=new List<EngineMoment>();
        void check_csurfaces() {} void get_moments() {} void get_rcs_characteristics() {}
        Vector3 get_sas_authority(){return Vector3.zero;}
        void get_engines(){engines.Clear();engines.AddRange(observed);}
        void update_velocity_acc() {} void update_aoa() {} void update_engine_moments() {}
        void get_gimbal_authority() {} void update_dynamics() {} void update_model_acc() {}
        void update_training_inputs() {} void update_cpu() {} void update_pitch_rot_model() {}
        void update_roll_rot_model() {} void update_yaw_rot_model() {}
        public void Tick(){OnPreAutopilot(new FlightCtrlState());postupdate_engine_balancing(new FlightCtrlState());}
        public void Observe(params float[] torques){observed.Clear();foreach(float t in torques)observed.Add(new EngineMoment(t));moments_cycle_counter=0;}
        public void Post(){postupdate_engine_balancing(new FlightCtrlState());}
        public void DiscoverOnly(params float[] torques){Observe(torques);get_engines();}
        public int Parameters {get{return optimizer==null?0:optimizer.x.rows;}}
        public int Columns {get{return optimizer==null?0:optimizer.A.cols;}}
        public float Limit(int i){return engines[i].engine.thrustPercentage;}
        public void SetLimit(int i,float v){engines[i].engine.thrustPercentage=v;}
        public void SeedLimiter(int i,double v){limiters[i]=v;}
        public void Steer(float v){gimbal_buf[0].value=v;}
        public static FlightModel Balanced(params float[] torques){var f=new FlightModel();f.balance_engines=true;f.Observe(torques);f.Tick();return f;}
        // Production update_moments / OnPreAutopilot are appended verbatim.
        __METHODS__
    }
    public static class Common { __COMMON__ }
}
class Test {
    static int passed,failed;
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Run(string name,Action body){try{body();passed++;Console.WriteLine("PASS: "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL: "+name+": "+e.GetType().Name+" "+e.Message);}}
    static void Bounded(AtmosphereAutopilot.FlightModel f,int n){for(int i=0;i<n;i++)Assert(!float.IsNaN(f.Limit(i))&&f.Limit(i)>=0&&f.Limit(i)<=100,"invalid limiter");}
    static int Main(){
        Run("OFF refresh 2 -> 3 then ON before next refresh",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10);
            f.balance_engines=false;f.Observe(10,-10,5);f.Tick();
            f.balance_engines=true;f.Tick();Assert(f.Parameters==3&&f.Columns==3,"stale optimizer size");Bounded(f,3);
        });
        Run("OFF refresh 3 -> 2 then ON before next refresh",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10,5);
            f.balance_engines=false;f.Observe(10,-10);f.Tick();
            f.balance_engines=true;f.Tick();Assert(f.Parameters==2&&f.Columns==2,"removed engine remains in optimizer");Bounded(f,2);
        });
        Run("first enable after disabled discovery",()=>{
            var f=new AtmosphereAutopilot.FlightModel();f.Observe(10,-10);f.Tick();
            f.balance_engines=true;f.Tick();Assert(f.Parameters==2,"balancing not initialized");Bounded(f,2);
        });
        Run("shrink then OFF regrowth within retained array capacity",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10,5);f.Observe(10,-10);f.Tick();
            f.balance_engines=false;f.Observe(10,-10,5);f.Tick();f.balance_engines=true;f.Tick();
            Assert(f.Parameters==3&&f.Columns==3,"capacity hid changed optimizer dimension");Bounded(f,3);
        });
        Run("OFF refresh to one engine preserves its existing limit",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10,5);f.SeedLimiter(0,.25);
            f.balance_engines=false;f.Observe(10);f.Tick();f.SetLimit(0,73);
            f.balance_engines=true;f.Tick();Assert(f.Limit(0)==73,"stale multi-engine limiter applied");
        });
        Run("post callback cannot apply stale storage after count change",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10);
            f.DiscoverOnly(10,-10,5);f.SetLimit(0,73);f.Post();Assert(f.Limit(0)==73,"partially wrote obsolete balance before next pre callback");
        });
        Run("enabled refresh grows and shrinks in same pre callback",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10);f.Observe(10,-10,5);f.Tick();
            Assert(f.Parameters==3,"growth");f.Observe(10,-10);f.Tick();Assert(f.Parameters==2,"shrink");Bounded(f,2);
        });
        Run("disabled balancing never writes throttle limit",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10);f.balance_engines=false;
            f.SetLimit(0,37);f.Tick();Assert(f.Limit(0)==37,"disabled write");
        });
        Run("stable symmetric pair and steering retain accepted values",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10);f.Tick();
            Assert(f.Limit(0)==100&&f.Limit(1)==100,"symmetric baseline");f.Steer(.2f);f.Post();
            Assert(Math.Abs(f.Limit(0)-100)<.001&&Math.Abs(f.Limit(1)-80)<.001,"steering baseline");
        });
        Run("zero engines after OFF refresh does not throw",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10,-10);f.balance_engines=false;f.Observe();f.Tick();
            f.balance_engines=true;f.Tick();
        });
        Run("empty initial vessel can later discover balanced engines",()=>{
            var f=new AtmosphereAutopilot.FlightModel();f.balance_engines=true;f.Tick();
            f.Observe(10,-10);f.Tick();Assert(f.Parameters==2,"late engines");Bounded(f,2);
        });
        Run("initial single engine keeps manually selected limit",()=>{
            var f=AtmosphereAutopilot.FlightModel.Balanced(10);f.SetLimit(0,42);f.Tick();
            Assert(f.Limit(0)==42,"single-engine write");
        });
        Console.WriteLine("R-AA2-01 ENGINE COUNT "+passed+"/"+(passed+failed)+" PASS");return failed==0?0:1;
    }
}
'''

methods = [method(AA / "Models/FlightModel/RotationModel.cs", "void update_moments()"),
           method(AA / "Models/FlightModel/FlightModel.cs", "void OnPreAutopilot(")]
common = [method(AA / "Common.cs", "public static void Realloc<T>("),
          method(AA / "Common.cs", "public static double Clamp(")]
with tempfile.TemporaryDirectory(prefix="aeris-aa2-count-") as folder:
    folder = Path(folder)
    fixture = folder / "Fixture.cs"
    fixture.write_text(HARNESS.replace("__METHODS__", "\n".join(methods))
                       .replace("__COMMON__", "\n".join(common)))
    exe = folder / "EngineCountTests.exe"
    subprocess.run(["mcs", "-unsafe", "-out:" + str(exe), str(fixture),
                    str(AA / "Models/FlightModel/EngineBalancing.cs"),
                    str(AA / "Math/GradientLP.cs"), str(AA / "Math/Matrix.cs")], check=True)
    raise SystemExit(subprocess.run(["mono", str(exe)]).returncode)
