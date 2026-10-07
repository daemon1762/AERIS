#!/usr/bin/env python3
"""Run complete production LinearModel/LinearSystemModel/Matrix sources.

Other FlightModel partials and KSP/Unity inputs are supplied at their boundary.
This tests generated model coefficients and evaluation, not a KSP flight.
Reintroducing a second mass division must fail the non-unit-mass cases.
"""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
AA = ROOT / "Source/AERISFlightControl/AA"

HARNESS = r'''
using System;
using System.Reflection;
using AtmosphereAutopilot;
namespace UnityEngine {}
public static class TimeWarp { public static float fixedDeltaTime=.02f; }
namespace AtmosphereAutopilot {
    public class AutopilotModule { protected const int PITCH=0,ROLL=1,YAW=2; }
    public static class Common {
        public static void Realloc<T>(ref T[] array,int size){if(array==null||array.Length<size)array=new T[size];}
    }
    public class VectorArray { public struct Vector {} }
    public static class AtmosphereAutopilot {
        public enum AerodinamycsModel { Stock,FAR }
        public static AerodinamycsModel AeroModel;
    }
    public static class SyncModuleControlSurface { public const double CSURF_SPD=2; }
    public class Parameters { public double[] pars=new double[5]; }
    public class InputHistory { public float getLast(){return 0;} }
    public sealed partial class FlightModel {
        public double dyn_pressure=600,surface_v_magnitude=100,gimbal_spd_norm=10;
        public float sum_mass=10;
        public bool any_gimbals,any_gimbal_spds;
        public double pitch_gravity_acc,pitch_noninert_acc,yaw_gravity_acc,yaw_noninert_acc;
        public double[] MOI={10,10,10},engines_torque_k0=new double[3],engines_torque_k1=new double[3],
            engines_thrust_k0=new double[3],engines_thrust_k1=new double[3],engines_thrust_principal=new double[3],
            reaction_torque=new double[3],rcs_authority_pos=new double[3],rcs_authority_neg=new double[3];
        public InputHistory[] input_buf={new InputHistory(),new InputHistory(),new InputHistory()};
        public Parameters pitch_lift_model=new Parameters(),pitch_aero_torque_model=new Parameters(),
            pitch_aero_torque_model_gen=new Parameters(),roll_aero_torque_model=new Parameters(),
            roll_aero_torque_model_gen=new Parameters(),yaw_lift_model=new Parameters(),
            yaw_aero_torque_model=new Parameters(),yaw_aero_torque_model_gen=new Parameters();
    }
}
class Test {
    static int passed,failed;
    static void Near(double actual,double expected){
        if(double.IsNaN(actual)||Math.Abs(actual-expected)>1e-8)
            throw new Exception("expected "+expected+", actual "+actual);
    }
    static void Run(string name,Action body){try{body();passed++;Console.WriteLine("PASS: "+name);}
        catch(Exception e){failed++;Console.WriteLine("FAIL: "+name+": "+e.Message);}}
    static void Update(FlightModel f,string axis="pitch"){
        typeof(FlightModel).GetMethod("update_"+axis+"_rot_model",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f,null);
    }
    static FlightModel Model(float mass=10,double thrust=20,double pressure=600,bool far=false){
        AtmosphereAutopilot.AtmosphereAutopilot.AeroModel=far?
            AtmosphereAutopilot.AtmosphereAutopilot.AerodinamycsModel.FAR:
            AtmosphereAutopilot.AtmosphereAutopilot.AerodinamycsModel.Stock;
        var f=new FlightModel();f.sum_mass=mass;f.engines_thrust_k0[0]=thrust;f.dyn_pressure=pressure;return f;
    }
    static void Check(float mass,double thrust,double pressure,double expected,bool far=false){
        var f=Model(mass,thrust,pressure,far);Update(f);
        Near(f.pitch_rot_model.C[0,0],expected);
        Near(f.pitch_rot_model_gen.C[0,0],expected);
        Near(f.pitch_rot_model_undelayed.C[0,0],expected);
        // Consumer-visible zero-state AoA derivative, evaluated by real model.
        Near(f.pitch_rot_model.eval_row(0,new Matrix(4,1),new Matrix(1,1)),expected);
    }
    static int Main(){
        // Literal expectations: transverse acceleration / 100 m/s, mass divided once.
        Run("10 mass / 20 thrust produces 0.02 AoA derivative",()=>Check(10,20,600,.02));
        Run("halving mass doubles thrust acceleration",()=>Check(5,20,600,.04));
        Run("doubling mass halves thrust acceleration",()=>Check(20,20,600,.01));
        Run("sub-unit mass uses only one division",()=>Check(.5f,20,600,.4));
        Run("negative transverse thrust preserves its sign",()=>Check(10,-20,600,-.02));
        Run("zero transverse thrust stays zero",()=>Check(10,0,600,0));
        Run("unit mass preserves accepted output",()=>Check(1,20,600,.2));
        Run("below pressure boundary remains unchanged",()=>Check(10,20,59.99,.02));
        Run("exact pressure boundary uses corrected output",()=>Check(10,20,60,.02));
        Run("FAR high-pressure model divides mass once",()=>Check(10,20,600,.02,true));
        Run("FAR low-pressure model remains unchanged",()=>Check(10,20,59.99,.02,true));
        Run("gravity inertial and lift accelerations retain additive contribution",()=>{
            var f=Model();f.pitch_gravity_acc=3;f.pitch_noninert_acc=1;f.pitch_lift_model.pars[0]=100;
            Update(f);Near(f.pitch_coeffs.et0,-2);Near(f.pitch_coeffs.Cl0,6);Near(f.pitch_rot_model.C[0,0],-.08);
        });
        Run("nonzero state evaluates corrected pitch derivative",()=>{
            var f=Model();f.pitch_gravity_acc=3;f.pitch_noninert_acc=1;
            f.pitch_lift_model.pars=new double[]{100,50,25,0,0};
            f.engines_thrust_principal[1]=100;f.engines_thrust_k1[0]=10;Update(f);
            var state=new Matrix(4,1);state[0,0]=.2;state[1,0]=.03;state[2,0]=.1;
            var input=new Matrix(1,1);input[0,0]=.5;
            Near(f.pitch_rot_model.eval_row(0,state,input),-.0725);
        });
        Run("gimbal lag and angular torque authority retain accepted coefficients",()=>{
            var f=Model();f.any_gimbals=f.any_gimbal_spds=true;
            f.engines_thrust_k1[0]=10;f.engines_torque_k0[0]=30;f.engines_torque_k1[0]=40;
            f.reaction_torque[0]=50;f.rcs_authority_pos[0]=10;Update(f);
            Near(f.pitch_rot_model.C[0,0],.02);Near(f.pitch_rot_model.C[1,0],3);
            // fixedDeltaTime is a float; account for its representation here only.
            if(Math.Abs(f.pitch_rot_model.A[1,3]-3.2)>1e-6||Math.Abs(f.pitch_rot_model.B[1,0]-6.8)>1e-6)
                throw new Exception("gimbal torque changed");
            Near(f.pitch_rot_model.A[0,3],.008);Near(f.pitch_rot_model.B[0,0],.002);
        });
        Run("yaw acceleration and torque remain unchanged",()=>{
            var f=Model();f.engines_thrust_k0[2]=20;f.engines_torque_k0[2]=30;
            Update(f,"yaw");Near(f.yaw_rot_model.C[0,0],-.02);Near(f.yaw_rot_model.C[1,0],3);
        });
        Run("roll torque remains unchanged",()=>{
            var f=Model();f.engines_torque_k0[1]=30;f.reaction_torque[1]=50;
            Update(f,"roll");Near(f.roll_rot_model.C[0,0],3);Near(f.roll_rot_model.B[0,0],5);
        });
        Console.WriteLine("R-AA2-03 PITCH THRUST "+passed+"/"+(passed+failed)+" PASS");return failed==0?0:1;
    }
}
'''

with tempfile.TemporaryDirectory(prefix="aeris-aa2-pitch-") as folder:
    folder = Path(folder)
    fixture = folder / "Fixture.cs"
    fixture.write_text(HARNESS)
    exe = folder / "PitchTests.exe"
    subprocess.run(["mcs", "-unsafe", "-out:" + str(exe), str(fixture),
                    str(AA / "Models/FlightModel/LinearModel.cs"),
                    str(AA / "Models/LinearSystemModel.cs"),
                    str(AA / "Math/Matrix.cs")], check=True)
    raise SystemExit(subprocess.run(["mono", str(exe)]).returncode)
