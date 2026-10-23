#!/usr/bin/env python3
"""Extract real FDI fitting/drawing/style setup; count only owned object creation.

Unity font measurement/GUI calls are boundary models, not whole-engine GC claims.
Compare fitted output to the frozen pre-fix algorithm, including exceptions and
source-style preservation. Reintroducing per-label clones must fail warmup counts.
"""
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'Source/AERISFlightControl/UI/AERISFlightInstrument.cs'
CASES = ['warm-style', 'warm-content', 'long-short', 'source-intact', 'multiple-sources',
         'resize', 'color', 'label-exception', 'measure-exception', 'tiny', 'parity']


def method(source, signature):
    start = source.index(signature)
    end = source.index('{', start)+1
    depth = 1
    while depth:
        depth += (source[end] == '{')-(source[end] == '}')
        end += 1
    return source[start:end]


# Frozen baseline algorithm (5c328312); separate from production candidate.
REFERENCE = r'''
static string Reference(GUIStyle style,string text,float width,int minimumFontSize){
 if(string.IsNullOrEmpty(text))return string.Empty;
 int start=Mathf.Max(minimumFontSize,style.fontSize);
 for(int size=start;size>=minimumFontSize;size--){style.fontSize=size;if(style.CalcSize(new GUIContent(text)).x<=width)return text;}
 const string ellipsis="...";string value=text;
 while(value.Length>1&&style.CalcSize(new GUIContent(value+ellipsis)).x>width)value=value.Substring(0,value.Length-1);
 return value.Length==text.Length?value:value+ellipsis;
}
'''

HARNESS = r'''
using System;
using System.Collections.Generic;
namespace AERIS53LabelTestHost {
struct Rect {internal float x,y,width,height;internal Rect(float a,float b,float c,float d){x=a;y=b;width=c;height=d;}}
struct Vector2 {internal float x;internal Vector2(float a){x=a;}}
struct Color {internal int Value;internal Color(int value){Value=value;}}
enum FontStyle {Normal,Bold} enum TextAnchor {MiddleLeft,MiddleCenter} enum TextClipping {Clip}
class GUIContent {internal static int Created;internal string text;internal GUIContent(){Created++;}internal GUIContent(string s){Created++;text=s;}}
class GUIStyle {
 internal static int Created;internal static bool ThrowMeasure;
 internal int fontSize;internal TextAnchor alignment;internal FontStyle fontStyle;internal TextClipping clipping;
 internal GUIStyle(){Created++;fontSize=12;}
 internal GUIStyle(GUIStyle source){Created++;fontSize=source.fontSize;alignment=source.alignment;fontStyle=source.fontStyle;clipping=source.clipping;}
 internal Vector2 CalcSize(GUIContent content){if(ThrowMeasure)throw new InvalidOperationException("fixture measure");return new Vector2((content.text??"").Length*fontSize);}
}
class Skin {internal GUIStyle label=new GUIStyle(),button=new GUIStyle();}
static class GUI {
 internal static Skin skin=new Skin();internal static Color color=new Color(9);internal static bool ThrowLabel;
 internal static List<string> Output=new List<string>();
 internal static void Label(Rect rect,string text,GUIStyle style){if(ThrowLabel)throw new InvalidOperationException("fixture label");Output.Add(text+"|"+style.fontSize+"|"+color.Value+"|"+rect.x+"|"+rect.y);}
}
static class Mathf {internal static float Abs(float v){return Math.Abs(v);}internal static int Max(int a,int b){return Math.Max(a,b);}internal static int RoundToInt(float v){return (int)Math.Round(v);}}
class Instrument {
 enum FdiTone {White,Green,Amber,Red}
 static readonly Color TextShadow=new Color(0),ReadableGreen=new Color(1),ReadableAmber=new Color(2),ReadableRed=new Color(3),ReadableWhite=new Color(4);
 GUIStyle labelStyle,smallStyle,titleStyle,panelTitleStyle,panelStyle;float styleScale=-1;
 // CACHE_FIELDS
 internal void Setup(float scale){EnsureStyles(scale);}
 internal GUIStyle Source {get{return labelStyle;}}
 internal void Draw(string s,float width,int minimum=7,GUIStyle source=null){DrawReadableLabel(new Rect(0,0,width,25),s,source??labelStyle,FdiTone.Green,minimum);}
 internal void Tiny(){DrawReadableLabel(new Rect(0,0,1,1),"tiny",labelStyle,FdiTone.Red,7);}
 // METHODS
}
public static class LabelCases {
 public static readonly string[] Names={"warm-style","warm-content","long-short","source-intact","multiple-sources","resize","color","label-exception","measure-exception","tiny","parity"};
 static void Assert(bool b,string why){if(!b)throw new Exception(why);}
 // REFERENCE
 public static void Run(string mode){
  GUI.Output.Clear();GUI.color=new Color(9);GUI.ThrowLabel=false;GUIStyle.ThrowMeasure=false;
  var i=new Instrument();i.Setup(1);i.Draw("warm",200);
  int styles=GUIStyle.Created,contents=GUIContent.Created;
  if(mode=="warm-style"){for(int n=0;n<1000;n++)i.Draw("warm",200);Assert(GUIStyle.Created==styles,"style allocated per label");}
  else if(mode=="warm-content"){for(int n=0;n<1000;n++)i.Draw("warm",200);Assert(GUIContent.Created==contents,"measurement content allocated per label");}
  else if(mode=="long-short"){i.Draw("abcdefghijklmnopqrstu",40);i.Draw("A",200);Assert(GUI.Output[GUI.Output.Count-1].StartsWith("A|12|"),"fitted shrink leaked into next label");}
  else if(mode=="source-intact"){i.Draw("abcdefghijklmnopqrstu",40);Assert(i.Source.fontSize==12,"source style was mutated");}
  else if(mode=="multiple-sources"){var other=new GUIStyle(i.Source);other.fontSize=16;i.Draw("A",200,7,other);i.Draw("A",200);Assert(GUI.Output[GUI.Output.Count-3].StartsWith("A|16|")&&GUI.Output[GUI.Output.Count-1].StartsWith("A|12|"),"source styles crossed");styles=GUIStyle.Created;for(int n=0;n<1000;n++){i.Draw("A",200,7,other);i.Draw("A",200);}Assert(GUIStyle.Created==styles,"alternating styles allocated");}
  else if(mode=="resize"){for(int n=0;n<100;n++){i.Setup(n%2==0?1:1.5f);i.Draw("A",200);}i.Setup(1.5f);i.Draw("A",200);styles=GUIStyle.Created;i.Draw("A",200);Assert(styles==GUIStyle.Created,"style not reused after resize");Assert(GUI.Output[GUI.Output.Count-1].StartsWith("A|18|"),"resized font changed");var cache=(System.Collections.IDictionary)typeof(Instrument).GetField("readableLabelStyles",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(i);Assert(cache.Count==1,"old scale styles retained");}
  else if(mode=="color"){i.Draw("A",200);Assert(GUI.color.Value==9,"GUI color leaked");Assert(GUI.Output[GUI.Output.Count-2].Split(new char[]{'|'})[2]=="0"&&GUI.Output[GUI.Output.Count-1].Split(new char[]{'|'})[2]=="1","shadow/tone changed");}
  else if(mode=="label-exception"){GUI.ThrowLabel=true;try{i.Draw("A",200);}catch(InvalidOperationException){}finally{GUI.ThrowLabel=false;}Assert(GUI.color.Value==9,"throwing label leaked color");}
  else if(mode=="measure-exception"){GUIStyle.ThrowMeasure=true;try{i.Draw("A",200);}catch(InvalidOperationException){}finally{GUIStyle.ThrowMeasure=false;}i.Draw("A",200);Assert(i.Source.fontSize==12&&GUI.Output[GUI.Output.Count-1].StartsWith("A|12|"),"measure failure poisoned next label");}
  else if(mode=="tiny"){int rows=GUI.Output.Count;i.Tiny();Assert(GUI.Output.Count==rows&&GUIStyle.Created==styles&&GUIContent.Created==contents,"tiny rect drew/allocated");}
  else if(mode=="parity"){
   foreach(string text in new[]{"short","abcdefghijklmnopqrstu","日本語の計器ラベル","",null})
   foreach(int width in new[]{3,10,40,120,400})foreach(int minimum in new[]{7,14}){
    var reference=new GUIStyle(i.Source);string expected=Reference(reference,text??"",width-2,minimum);i.Draw(text,width,minimum);
    string row=GUI.Output[GUI.Output.Count-1];Assert(row.StartsWith(expected+"|"+reference.fontSize+"|"),"font/text parity changed: "+row);
   }
  }else throw new Exception("unknown case");
 }
#if !AUDIT_LIB
 static int Main(string[] args){try{Run(args[0]);Console.WriteLine("PASS: "+args[0]);return 0;}catch(Exception e){Console.Error.WriteLine("FAIL: "+args[0]+": "+e.Message);return 1;}}
#endif
}
}
'''


def fixture_source():
    source = SOURCE.read_text()
    signatures = ['void EnsureStyles(', 'void DrawReadableLabel(', 'static Color ToneColor(']
    signatures.append('static string FitText(' if 'static string FitText(' in source else 'string FitText(')
    if 'float MeasureLabelWidth(' in source:
        signatures.append('float MeasureLabelWidth(')
    fields = '\n'.join(re.findall(r'readonly (?:Dictionary<GUIStyle, GUIStyle>|GUIContent) (?:readableLabelStyles|fittingContent)[^;]*;', source))
    return HARNESS.replace('// CACHE_FIELDS', fields).replace('// METHODS', '\n'.join(
        method(source, s) for s in signatures)).replace('// REFERENCE', REFERENCE)


if __name__ == '__main__':
    with tempfile.TemporaryDirectory(prefix='aeris-label-') as td:
        p = Path(td);(p/'Fixture.cs').write_text(fixture_source())
        subprocess.run(['mcs', '-warn:0', '-out:'+str(p/'Cases.exe'), str(p/'Fixture.cs')], check=True)
        results = [subprocess.run(['mono', str(p/'Cases.exe'), c]).returncode for c in CASES]
        print(f'R-ND-02 LABEL OBJECTS {results.count(0)}/{len(CASES)} PASS')
        raise SystemExit(0 if not any(results) else 1)
