#!/usr/bin/env python3
"""Exercise both complete production delayed-coordinate classes via UI input.

Only Unity text entry, clock and logging are stubbed. No game files are touched.
Requires mcs/mono; does not claim a real KSP GUI or navigation test.
"""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source/AERISFlightControl/AA/GUI/DelayedField"

HARNESS = r'''
using System;
using System.Globalization;
using AtmosphereAutopilot;
using UnityEngine;
namespace UnityEngine {
    public class GUIStyle {}
    public class GUILayoutOption {}
    public static class GUILayout {
        public static string Next;
        public static string TextField(string current,GUIStyle style,params GUILayoutOption[] options) {
            if(Next==null)return current;
            string next=Next;Next=null;return next;
        }
    }
    public static class Time {public static float deltaTime;}
    public static class Debug {public static void LogFormat(string f,params object[] args) {}}
}
class Test {
    static int passed,failed;
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Near(float actual,double expected) {
        Assert(!float.IsNaN(actual)&&Math.Abs(actual-expected)<.00001,
            "expected "+expected.ToString("R",CultureInfo.InvariantCulture)+", actual "+actual.ToString("R",CultureInfo.InvariantCulture));
    }
    static void Run(string name,Action body){try{body();passed++;Console.WriteLine("PASS: "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL: "+name+": "+e.Message);}}
    static DelayedFieldGeoCoordinates Field(DelayedFieldGeoCoordinates.CoordFormat mode){return new DelayedFieldGeoCoordinates(1.25f,"#0.0000",mode);}
    static void Enter(DelayedFieldGeoCoordinates field,string text){GUILayout.Next=text;field.DisplayLayout(new GUIStyle());}
    static void Advance(DelayedFieldGeoCoordinates field,float seconds){Time.deltaTime=seconds;field.OnUpdate();}
    static void Parse(string text,DelayedFieldGeoCoordinates.CoordFormat mode,double expected,bool formatted=true){
        var field=Field(mode);Enter(field,text);Advance(field,2);Near(field.Value,expected);
        Assert(field.input_str==(formatted?field.Value.ToString(field.format_str):text),"numeric feedback mismatch");
    }
    static int Main(){
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        var NS=DelayedFieldGeoCoordinates.CoordFormat.NS;
        var EW=DelayedFieldGeoCoordinates.CoordFormat.EW;
        var Decimal=DelayedFieldGeoCoordinates.CoordFormat.Decimal;
        // Literal expectations are hand-derived, independent of the parser.
        Run("N reads distinct seconds",()=>Parse("12 34 56N",NS,12.582222222222));
        Run("S reads distinct seconds with negative sign",()=>Parse("12 34 56S",NS,-12.582222222222));
        Run("E reads distinct seconds",()=>Parse("74 39 12E",EW,74.653333333333));
        Run("W reads distinct seconds with negative sign",()=>Parse("74 39 12W",EW,-74.653333333333));
        Run("lowercase n remains accepted",()=>Parse("12 34 56n",NS,12.582222222222));
        Run("lowercase s remains accepted",()=>Parse("12 34 56s",NS,-12.582222222222));
        Run("lowercase e remains accepted",()=>Parse("74 39 12e",EW,74.653333333333));
        Run("lowercase w remains accepted",()=>Parse("74 39 12w",EW,-74.653333333333));
        Run("zero minutes do not erase nonzero seconds",()=>Parse("0 0 1W",EW,-.000277777777778));
        Run("zero seconds do not repeat nonzero minutes",()=>Parse("30 45 0E",EW,30.75));
        Run("degrees and minutes without seconds remain accepted",()=>Parse("12 34N",NS,12.566666666667));
        Run("degrees only remain accepted",()=>Parse("74W",EW,-74));
        Run("signed decimal latitude is unchanged",()=>Parse("-12.125",NS,-12.125,false));
        Run("decimal-mode longitude is unchanged",()=>Parse("-74.125",Decimal,-74.125,false));
        Run("unsigned decimal longitude is unchanged",()=>Parse("74.125",EW,74.125,false));
        Run("DMS commits only after delay and then stays stable",()=>{
            var field=Field(NS);Enter(field,"12 34 56N");Advance(field,1.5f);Near(field.Value,1.25);
            Advance(field,.5f);Near(field.Value,12.582222222222);string feedback=field.input_str;
            Advance(field,10);Near(field.Value,12.582222222222);Assert(field.input_str==feedback,"idle input changed");
        });
        Run("further editing restarts the input delay",()=>{
            var field=Field(EW);Enter(field,"74 39 12W");Advance(field,1.5f);
            Enter(field,"74 39 0W");Advance(field,.5f);Near(field.Value,1.25);
            Advance(field,1.5f);Near(field.Value,-74.65);
        });
        Run("invalid decimal restores the prior numeric input",()=>{
            var field=Field(NS);Enter(field,"invalid");Advance(field,2);Near(field.Value,1.25);
            Assert(field.input_str=="1.2500","invalid input feedback changed");
        });
        Run("numeric setter used by waypoints bypasses DMS parsing",()=>{
            var field=Field(EW);field.Value=-74.125f;Advance(field,2);Near(field.Value,-74.125);
            Assert(field.input_str=="-74.1250","setter feedback changed");
        });
        Run("current-culture decimal comma remains supported",()=>{
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            try{Parse("-12,125",NS,-12.125,false);}finally{CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;}
        });
        Console.WriteLine("R-AA2-02 COORDINATES "+passed+"/"+(passed+failed)+" PASS");return failed==0?0:1;
    }
}
'''

with tempfile.TemporaryDirectory(prefix="aeris-aa2-coordinates-") as folder:
    folder = Path(folder)
    fixture = folder / "Fixture.cs"
    fixture.write_text(HARNESS)
    exe = folder / "CoordinateTests.exe"
    subprocess.run(["mcs", "-out:" + str(exe), str(fixture),
                    str(SOURCE / "DelayedFieldFloat.cs"),
                    str(SOURCE / "DelayedFieldGeoCoordinates.cs")], check=True)
    raise SystemExit(subprocess.run(["mono", str(exe)]).returncode)
