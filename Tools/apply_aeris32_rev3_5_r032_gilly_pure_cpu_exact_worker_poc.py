#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'Source/AERISFlightControl/Terrain'
SOURCE=SRC/'AERISR032PtcGillyPureCpuExactWorkerObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT='AERIS32_REV3_5_R031_PTC_GILLY_DEPENDENCY_CLOSURE_SHADOW'
MARKER='AERIS32_REV3_5_R032_PTC_GILLY_PURE_CPU_EXACT_WORKER_SHADOW'
OBSERVER=r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R032_PTC_GILLY_PURE_CPU_EXACT_WORKER_SHADOW
    // Shadow-only proof of concept. Main thread snapshots KSP/PQS runtime state and truth.
    // Worker consumes only copied primitive arrays/scalars. It never touches Unity/KSP/PQS,
    // reflection, logging, terrain DB, producer authority, or GPU state.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR032PtcGillyPureCpuExactWorkerObserver : MonoBehaviour
    {
        const double PrimitiveTolerance = 1e-12;
        const double TerrainToleranceMeters = 1e-8;
        float nextAttempt;
        bool started;
        bool reported;
        int mainThreadId;
        Snapshot snapshot;
        volatile WorkerResult completed;

        struct Point3
        {
            internal double X, Y, Z;
            internal Point3(double x, double y, double z) { X=x; Y=y; Z=z; }
        }

        sealed class Snapshot
        {
            internal int[] Perm;
            internal int[] Grad3;
            internal double[] RandomVectors;
            internal double[] SpectralWeights;
            internal double SimplexFrequency;
            internal int SimplexOctaves;
            internal double SimplexPersistence;
            internal double SimplexDeformity;
            internal double RidgedFrequency;
            internal double RidgedLacunarity;
            internal int RidgedOctaves;
            internal int RidgedSeed;
            internal int RidgedQuality;
            internal double RidgedDeformity;
            internal Point3[] PrimitivePoints;
            internal double[] NativeSimplex;
            internal double[] NativeRidged;
            internal Point3[] TerrainPoints;
            internal double[] PqsTerrainMeters;
        }

        sealed class WorkerResult
        {
            internal int ThreadId;
            internal double[] Simplex;
            internal double[] Ridged;
            internal double[] Terrain;
            internal string Error;
        }

        void Update()
        {
            if (reported) return;
            if (!started)
            {
                if (Time.realtimeSinceStartup < nextAttempt) return;
                nextAttempt = Time.realtimeSinceStartup + 1f;
                if (!AERISTerrainTileSystem.GameDataHashReady || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
                TryStart();
                return;
            }
            WorkerResult r=completed;
            if (r==null) return;
            reported=true;
            Report(r);
        }

        void TryStart()
        {
            mainThreadId=Thread.CurrentThread.ManagedThreadId;
            try
            {
                Snapshot s=CaptureSnapshot();
                if (s==null) return;
                snapshot=s;
                started=true;
                AERISLogger.Info("[R032][PTC_GILLY_CPU] event=SNAPSHOT_COMPLETE"+
                    "; main_thread_id="+mainThreadId+
                    "; perm="+s.Perm.Length+"; grad3="+s.Grad3.Length+
                    "; random_vectors="+s.RandomVectors.Length+"; spectral_weights="+s.SpectralWeights.Length+
                    "; primitive_points="+s.PrimitivePoints.Length+"; terrain_points="+s.TerrainPoints.Length+
                    "; simplex_frequency="+F(s.SimplexFrequency)+"; simplex_octaves="+s.SimplexOctaves+
                    "; simplex_persistence="+F(s.SimplexPersistence)+"; simplex_deformity="+F(s.SimplexDeformity)+
                    "; ridged_frequency="+F(s.RidgedFrequency)+"; ridged_lacunarity="+F(s.RidgedLacunarity)+
                    "; ridged_octaves="+s.RidgedOctaves+"; ridged_seed="+s.RidgedSeed+
                    "; ridged_quality="+s.RidgedQuality+"; ridged_deformity="+F(s.RidgedDeformity)+
                    "; snapshot_payload=PRIMITIVES_ONLY; worker_invokes_runtime_object=false; authority=PQS");
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    WorkerResult result=new WorkerResult();
                    result.ThreadId=Thread.CurrentThread.ManagedThreadId;
                    try { EvaluateWorker((Snapshot)state,result); }
                    catch(Exception ex) { result.Error=ex.GetType().FullName+":"+ex.Message; }
                    Thread.MemoryBarrier();
                    completed=result;
                },s);
            }
            catch(Exception ex)
            {
                started=true; reported=true;
                AERISLogger.Info("[R032][PTC_GILLY_CPU_FAIL] stage=SNAPSHOT; error="+Safe(ex.GetType().Name+":"+ex.Message)+
                    "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
            }
        }

        Snapshot CaptureSnapshot()
        {
            CelestialBody gilly=null;
            for(int i=0;i<FlightGlobals.Bodies.Count;i++)
            {
                CelestialBody b=FlightGlobals.Bodies[i];
                if(b!=null && string.Equals(b.name,"Gilly",StringComparison.OrdinalIgnoreCase)){gilly=b;break;}
            }
            if(gilly==null) throw new InvalidOperationException("Gilly not found");
            object pqs=ReadMember(gilly,"pqsController");
            if(pqs==null) throw new InvalidOperationException("Gilly PQS missing");
            IEnumerable mods=ReadMember(pqs,"mods") as IEnumerable;
            if(mods==null) throw new InvalidOperationException("Gilly PQS mods missing");
            object simplexMod=null, heightNoiseMod=null;
            foreach(object mod in mods)
            {
                if(mod==null) continue;
                string n=mod.GetType().FullName??mod.GetType().Name;
                if(n=="PQSMod_VertexSimplexHeightAbsolute") simplexMod=mod;
                else if(n=="PQSMod_VertexHeightNoise") heightNoiseMod=mod;
            }
            if(simplexMod==null || heightNoiseMod==null) throw new InvalidOperationException("Gilly height modifiers missing");
            object simplex=ReadMember(simplexMod,"simplex");
            object ridged=ReadMember(heightNoiseMod,"noiseMap");
            if(simplex==null || ridged==null) throw new InvalidOperationException("Gilly runtime noise objects missing");

            Snapshot s=new Snapshot();
            s.Perm=CopyIntArray(ReadMember(simplex,"perm"),"Simplex.perm");
            FieldInfo gradField=simplex.GetType().GetField("grad3",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(gradField==null) throw new InvalidOperationException("Simplex.grad3 missing");
            s.Grad3=CopyIntArray(gradField.GetValue(null),"Simplex.grad3");
            Assembly lib=ridged.GetType().Assembly;
            Type basis=lib.GetType("LibNoise.GradientNoiseBasis",false);
            if(basis==null) throw new InvalidOperationException("LibNoise.GradientNoiseBasis missing");
            FieldInfo vectorsField=basis.GetField("RandomVectors",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(vectorsField==null) throw new InvalidOperationException("RandomVectors missing");
            s.RandomVectors=CopyDoubleArray(vectorsField.GetValue(null),"RandomVectors");
            s.SpectralWeights=CopyDoubleArray(ReadMember(ridged,"SpectralWeights"),"SpectralWeights");

            s.SimplexFrequency=ReadDouble(simplex,"frequency");
            s.SimplexOctaves=ReadInt(simplex,"octaves");
            s.SimplexPersistence=ReadDouble(simplex,"persistence");
            s.SimplexDeformity=ReadDouble(simplexMod,"deformity");
            s.RidgedFrequency=ReadDouble(ridged,"Frequency");
            s.RidgedLacunarity=ReadDouble(ridged,"Lacunarity");
            s.RidgedOctaves=ReadInt(ridged,"OctaveCount");
            s.RidgedSeed=ReadInt(ridged,"Seed");
            s.RidgedQuality=ReadInt(ridged,"NoiseQuality");
            s.RidgedDeformity=ReadDouble(heightNoiseMod,"deformity");

            if(s.Perm.Length!=512) throw new InvalidOperationException("unexpected Simplex.perm length "+s.Perm.Length);
            if(s.Grad3.Length!=36) throw new InvalidOperationException("unexpected Simplex.grad3 length "+s.Grad3.Length);
            if(s.RandomVectors.Length!=1024) throw new InvalidOperationException("unexpected RandomVectors length "+s.RandomVectors.Length);
            if(s.SpectralWeights.Length<Math.Max(30,s.RidgedOctaves)) throw new InvalidOperationException("unexpected SpectralWeights length "+s.SpectralWeights.Length);

            s.PrimitivePoints=new Point3[]{
                new Point3(0.0,0.0,0.0),
                new Point3(0.1,0.2,0.3),
                new Point3(-0.25,0.5,-0.75),
                new Point3(1.0,2.0,3.0),
                new Point3(12.345,-67.89,0.125),
                new Point3(-Math.PI,Math.E,0.577215664901533)
            };
            s.NativeSimplex=new double[s.PrimitivePoints.Length];
            s.NativeRidged=new double[s.PrimitivePoints.Length];
            MethodInfo simplexNoise=FindTripleDoubleMethod(simplex.GetType(),"noise");
            MethodInfo ridgedValue=FindTripleDoubleMethod(ridged.GetType(),"GetValue");
            for(int i=0;i<s.PrimitivePoints.Length;i++)
            {
                Point3 p=s.PrimitivePoints[i];
                s.NativeSimplex[i]=Convert.ToDouble(simplexNoise.Invoke(simplex,new object[]{p.X,p.Y,p.Z}),CultureInfo.InvariantCulture);
                s.NativeRidged[i]=Convert.ToDouble(ridgedValue.Invoke(ridged,new object[]{p.X,p.Y,p.Z}),CultureInfo.InvariantCulture);
            }

            s.TerrainPoints=BuildTerrainPoints();
            s.PqsTerrainMeters=new double[s.TerrainPoints.Length];
            MethodInfo surface=FindVector3dMethod(pqs.GetType(),"GetSurfaceHeight");
            double radius=ReadDoubleOrDefault(pqs,"radius",gilly.Radius);
            for(int i=0;i<s.TerrainPoints.Length;i++)
            {
                Point3 p=s.TerrainPoints[i];
                object raw=surface.Invoke(pqs,new object[]{new Vector3d(p.X,p.Y,p.Z)});
                s.PqsTerrainMeters[i]=Convert.ToDouble(raw,CultureInfo.InvariantCulture)-radius;
            }
            return s;
        }

        static Point3[] BuildTerrainPoints()
        {
            Point3[] p=new Point3[]{
                new Point3(1,0,0),new Point3(0,1,0),new Point3(0,0,1),
                new Point3(-1,0,0),new Point3(0,-1,0),new Point3(0,0,-1),
                new Point3(1,1,1),new Point3(-1,1,1),new Point3(1,-1,1),
                new Point3(1,1,-1),new Point3(0.123,-0.456,0.789),new Point3(-0.731,0.419,0.538)
            };
            for(int i=0;i<p.Length;i++)
            {
                double m=Math.Sqrt(p[i].X*p[i].X+p[i].Y*p[i].Y+p[i].Z*p[i].Z);
                p[i]=new Point3(p[i].X/m,p[i].Y/m,p[i].Z/m);
            }
            return p;
        }

        static void EvaluateWorker(Snapshot s,WorkerResult r)
        {
            r.Simplex=new double[s.PrimitivePoints.Length];
            r.Ridged=new double[s.PrimitivePoints.Length];
            for(int i=0;i<s.PrimitivePoints.Length;i++)
            {
                Point3 p=s.PrimitivePoints[i];
                r.Simplex[i]=SimplexNoise(s,p.X,p.Y,p.Z);
                r.Ridged[i]=RidgedValue(s,p.X,p.Y,p.Z);
            }
            r.Terrain=new double[s.TerrainPoints.Length];
            for(int i=0;i<s.TerrainPoints.Length;i++)
            {
                Point3 p=s.TerrainPoints[i];
                double simplex=SimplexNoise(s,p.X,p.Y,p.Z);
                double ridged=RidgedValue(s,p.X,p.Y,p.Z);
                // Exact modifier IL:
                // VertexSimplexHeightAbsolute: vertHeight += (noise + 1) * 0.5 * deformity
                // VertexHeightNoise:          vertHeight += GetValue(...) * deformity
                r.Terrain[i]=(simplex+1.0)*0.5*s.SimplexDeformity + ridged*s.RidgedDeformity;
            }
        }

        void Report(WorkerResult r)
        {
            if(!string.IsNullOrEmpty(r.Error))
            {
                AERISLogger.Info("[R032][PTC_GILLY_CPU_FAIL] stage=WORKER; main_thread_id="+mainThreadId+
                    "; worker_thread_id="+r.ThreadId+"; error="+Safe(r.Error)+
                    "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
                return;
            }
            Snapshot s=snapshot;
            if(s==null)
            {
                AERISLogger.Info("[R032][PTC_GILLY_CPU_FAIL] stage=REPORT; error=SNAPSHOT_MISSING; authority=PQS");
                return;
            }
            int primitiveFailures=0, terrainFailures=0;
            double maxPrimitive=0.0,maxTerrain=0.0;
            int primitiveCount=Math.Min(r.Simplex.Length,s.NativeSimplex.Length);
            for(int i=0;i<primitiveCount;i++)
            {
                double ds=Math.Abs(r.Simplex[i]-s.NativeSimplex[i]);
                double dr=Math.Abs(r.Ridged[i]-s.NativeRidged[i]);
                if(ds>maxPrimitive)maxPrimitive=ds;if(dr>maxPrimitive)maxPrimitive=dr;
                if(ds>PrimitiveTolerance)primitiveFailures++;
                if(dr>PrimitiveTolerance)primitiveFailures++;
                AERISLogger.Info("[R032][PTC_GILLY_CPU_PRIMITIVE] index="+i+
                    "; simplex_native="+F(s.NativeSimplex[i])+"; simplex_worker="+F(r.Simplex[i])+"; simplex_abs_error="+F(ds)+
                    "; ridged_native="+F(s.NativeRidged[i])+"; ridged_worker="+F(r.Ridged[i])+"; ridged_abs_error="+F(dr)+
                    "; worker_invokes_runtime_object=false; authority=PQS");
            }
            int terrainCount=Math.Min(r.Terrain.Length,s.PqsTerrainMeters.Length);
            for(int i=0;i<terrainCount;i++)
            {
                double d=Math.Abs(r.Terrain[i]-s.PqsTerrainMeters[i]); if(d>maxTerrain)maxTerrain=d;
                if(d>TerrainToleranceMeters)terrainFailures++;
                Point3 p=s.TerrainPoints[i];
                AERISLogger.Info("[R032][PTC_GILLY_CPU_TERRAIN] index="+i+
                    "; x="+F(p.X)+"; y="+F(p.Y)+"; z="+F(p.Z)+
                    "; pqs_truth_m="+F(s.PqsTerrainMeters[i])+"; worker_m="+F(r.Terrain[i])+"; abs_error_m="+F(d)+
                    "; worker_invokes_runtime_object=false; authority=PQS");
            }
            bool offMain=r.ThreadId!=0 && r.ThreadId!=mainThreadId;
            AERISLogger.Info("[R032][PTC_GILLY_CPU] event=PURE_CPU_EXACT_WORKER_COMPLETE"+
                "; main_thread_id="+mainThreadId+"; worker_thread_id="+r.ThreadId+"; worker_off_main="+offMain+
                "; primitive_pairs="+(primitiveCount*2)+"; primitive_failures="+primitiveFailures+
                "; max_primitive_abs_error="+F(maxPrimitive)+
                "; terrain_points="+terrainCount+"; terrain_failures="+terrainFailures+
                "; max_terrain_abs_error_m="+F(maxTerrain)+
                "; primitive_tolerance="+F(PrimitiveTolerance)+"; terrain_tolerance_m="+F(TerrainToleranceMeters)+
                "; snapshot_payload=PRIMITIVES_ONLY; runtime_object_invocation_thread=MAIN_THREAD_ONLY"+
                "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static double SimplexNoise(Snapshot s,double x,double y,double z)
        {
            double result=0.0,max=0.0,frequency=s.SimplexFrequency,amplitude=1.0;
            int itr=0;
            while(itr<s.SimplexOctaves)
            {
                result+=SimplexValue(s,x*frequency,y*frequency,z*frequency)*amplitude;
                frequency*=2.0; max+=amplitude; amplitude*=s.SimplexPersistence; itr++;
            }
            return result/max;
        }

        static double SimplexValue(Snapshot s,double x,double y,double z)
        {
            const double F3=1.0/3.0,G3=1.0/6.0;
            double skew=(x+y+z)*F3;
            int i=FastFloor(x+skew),j=FastFloor(y+skew),k=FastFloor(z+skew);
            double unskew=(i+j+k)*G3;
            double x0=x-(i-unskew),y0=y-(j-unskew),z0=z-(k-unskew);
            int i1,j1,k1,i2,j2,k2;
            if(x0>=y0)
            {
                if(y0>=z0){i1=1;j1=0;k1=0;i2=1;j2=1;k2=0;}
                else if(x0>=z0){i1=1;j1=0;k1=0;i2=1;j2=0;k2=1;}
                else{i1=0;j1=0;k1=1;i2=1;j2=0;k2=1;}
            }
            else
            {
                if(y0<z0){i1=0;j1=0;k1=1;i2=0;j2=1;k2=1;}
                else if(x0<z0){i1=0;j1=1;k1=0;i2=0;j2=1;k2=1;}
                else{i1=0;j1=1;k1=0;i2=1;j2=1;k2=0;}
            }
            double x1=x0-i1+G3,y1=y0-j1+G3,z1=z0-k1+G3;
            double x2=x0-i2+2.0*G3,y2=y0-j2+2.0*G3,z2=z0-k2+2.0*G3;
            double x3=x0-1.0+3.0*G3,y3=y0-1.0+3.0*G3,z3=z0-1.0+3.0*G3;
            int ii=i&255,jj=j&255,kk=k&255;
            int gi0=s.Perm[ii+s.Perm[jj+s.Perm[kk]]]%12;
            int gi1=s.Perm[ii+i1+s.Perm[jj+j1+s.Perm[kk+k1]]]%12;
            int gi2=s.Perm[ii+i2+s.Perm[jj+j2+s.Perm[kk+k2]]]%12;
            int gi3=s.Perm[ii+1+s.Perm[jj+1+s.Perm[kk+1]]]%12;
            double n0=Corner(s,gi0,x0,y0,z0),n1=Corner(s,gi1,x1,y1,z1),n2=Corner(s,gi2,x2,y2,z2),n3=Corner(s,gi3,x3,y3,z3);
            return 32.0*(n0+n1+n2+n3);
        }

        static double Corner(Snapshot s,int gi,double x,double y,double z)
        {
            double t=0.6-x*x-y*y-z*z; if(t<0.0)return 0.0; t*=t;
            int o=gi*3; return t*t*(s.Grad3[o]*x+s.Grad3[o+1]*y+s.Grad3[o+2]*z);
        }

        static int FastFloor(double x){return x<=0.0?(int)x-1:(int)x;}

        static double RidgedValue(Snapshot s,double x,double y,double z)
        {
            x*=s.RidgedFrequency;y*=s.RidgedFrequency;z*=s.RidgedFrequency;
            double signal=0.0,value=0.0,weight=1.0,offset=1.0,gain=2.0;
            for(int octave=0;octave<s.RidgedOctaves;octave++)
            {
                int seed=(s.RidgedSeed+octave)&0x7fffffff;
                signal=GradientCoherentNoise(s,x,y,z,seed,s.RidgedQuality);
                signal=Math.Abs(signal); signal=offset-signal; signal*=signal; signal*=weight;
                weight=signal*gain; if(weight>1.0)weight=1.0; if(weight<0.0)weight=0.0;
                value+=signal*s.SpectralWeights[octave];
                x*=s.RidgedLacunarity;y*=s.RidgedLacunarity;z*=s.RidgedLacunarity;
            }
            return value*1.25-1.0;
        }

        static double GradientCoherentNoise(Snapshot s,double x,double y,double z,int seed,int quality)
        {
            int x0=x>0.0?(int)x:(int)x-1,x1=x0+1;
            int y0=y>0.0?(int)y:(int)y-1,y1=y0+1;
            int z0=z>0.0?(int)z:(int)z-1,z1=z0+1;
            double xs=x-x0,ys=y-y0,zs=z-z0;
            if(quality==1){xs=SCurve3(xs);ys=SCurve3(ys);zs=SCurve3(zs);}
            else if(quality==2){xs=SCurve5(xs);ys=SCurve5(ys);zs=SCurve5(zs);}
            double n0=GradientNoise(s,x,y,z,x0,y0,z0,seed),n1=GradientNoise(s,x,y,z,x1,y0,z0,seed);
            double ix0=LinearInterpolate(n0,n1,xs);
            n0=GradientNoise(s,x,y,z,x0,y1,z0,seed);n1=GradientNoise(s,x,y,z,x1,y1,z0,seed);
            double ix1=LinearInterpolate(n0,n1,xs),iy0=LinearInterpolate(ix0,ix1,ys);
            n0=GradientNoise(s,x,y,z,x0,y0,z1,seed);n1=GradientNoise(s,x,y,z,x1,y0,z1,seed);
            ix0=LinearInterpolate(n0,n1,xs);
            n0=GradientNoise(s,x,y,z,x0,y1,z1,seed);n1=GradientNoise(s,x,y,z,x1,y1,z1,seed);
            ix1=LinearInterpolate(n0,n1,xs);
            double iy1=LinearInterpolate(ix0,ix1,ys);
            return LinearInterpolate(iy0,iy1,zs);
        }

        static double GradientNoise(Snapshot s,double fx,double fy,double fz,int ix,int iy,int iz,long seed)
        {
            int spatial=unchecked(1619*ix+31337*iy+6971*iz);
            long vectorIndex=(unchecked((long)spatial)+1013L*seed)&0xffffffffL;
            vectorIndex^=(vectorIndex>>8); vectorIndex&=255L;
            int o=(int)(vectorIndex<<2);
            double xv=s.RandomVectors[o],yv=s.RandomVectors[o+1],zv=s.RandomVectors[o+2];
            double dx=fx-ix,dy=fy-iy,dz=fz-iz;
            return (xv*dx+yv*dy+zv*dz)*2.12;
        }

        static double LinearInterpolate(double a,double b,double p){return (1.0-p)*a+p*b;}
        static double SCurve3(double a){return a*a*(3.0-2.0*a);}
        static double SCurve5(double a){double a3=a*a*a,a4=a3*a,a5=a4*a;return 6.0*a5-15.0*a4+10.0*a3;}

        static MethodInfo FindTripleDoubleMethod(Type t,string name)
        {
            MethodInfo m=t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(double),typeof(double),typeof(double)},null);
            if(m==null)throw new MissingMethodException(t.FullName,name+"(double,double,double)"); return m;
        }
        static MethodInfo FindVector3dMethod(Type t,string name)
        {
            MethodInfo m=t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(Vector3d)},null);
            if(m==null)throw new MissingMethodException(t.FullName,name+"(Vector3d)"); return m;
        }
        static object ReadMember(object target,string name)
        {
            if(target==null)return null; Type t=target.GetType();
            FieldInfo f=t.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); if(f!=null)return f.GetValue(f.IsStatic?null:target);
            PropertyInfo p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(p==null||!p.CanRead||p.GetIndexParameters().Length!=0)return null; MethodInfo g=p.GetGetMethod(true); return p.GetValue(g!=null&&g.IsStatic?null:target,null);
        }
        static double ReadDouble(object target,string name){object v=ReadMember(target,name);if(v==null)throw new MissingMemberException(target.GetType().FullName,name);return Convert.ToDouble(v,CultureInfo.InvariantCulture);}
        static double ReadDoubleOrDefault(object target,string name,double fallback){object v=ReadMember(target,name);return v==null?fallback:Convert.ToDouble(v,CultureInfo.InvariantCulture);}
        static int ReadInt(object target,string name){object v=ReadMember(target,name);if(v==null)throw new MissingMemberException(target.GetType().FullName,name);return Convert.ToInt32(v,CultureInfo.InvariantCulture);}
        static int[] CopyIntArray(object value,string name)
        {
            Array a=value as Array;if(a==null)throw new InvalidOperationException(name+" missing");List<int> d=new List<int>();FlattenInt(a,d);return d.ToArray();
        }
        static void FlattenInt(Array a,List<int> d){for(int i=0;i<a.Length;i++){object v=a.GetValue(i);Array n=v as Array;if(n!=null)FlattenInt(n,d);else d.Add(Convert.ToInt32(v,CultureInfo.InvariantCulture));}}
        static double[] CopyDoubleArray(object value,string name)
        {
            Array a=value as Array;if(a==null)throw new InvalidOperationException(name+" missing");double[] d=new double[a.Length];for(int i=0;i<a.Length;i++)d[i]=Convert.ToDouble(a.GetValue(i),CultureInfo.InvariantCulture);return d;
        }
        static string F(double v){return v.ToString("R",CultureInfo.InvariantCulture);}
        static string Safe(string s){return string.IsNullOrEmpty(s)?string.Empty:s.Replace('\n',' ').Replace('\r',' ').Replace(';',',').Replace('|','/');}
    }
}
'''
SOURCE.parent.mkdir(parents=True,exist_ok=True)
if SOURCE.exists() and SOURCE.read_text()!=OBSERVER: raise SystemExit('R032 Gilly pure CPU worker observer exists with unexpected content')
SOURCE.write_text(OBSERVER)
cs=CSPROJ.read_text(); inc='    <Compile Include="Terrain\\AERISR032PtcGillyPureCpuExactWorkerObserver.cs" />\n'; anchor='    <Compile Include="Terrain\\AERISR031PtcGillyDependencyClosureObserver.cs" />\n'
if inc not in cs:
    if anchor not in cs: raise SystemExit('R031 Gilly dependency closure csproj anchor missing; materialize accepted R031 first')
    CSPROJ.write_text(cs.replace(anchor,anchor+inc,1))
version=VERSION.read_text()
if PARENT not in version and MARKER not in version: raise SystemExit('R031 Gilly dependency closure parent identity missing; materialize accepted R031 first')
version=version.replace('AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC GILLY DEPENDENCY CLOSURE SHADOW','AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R032 PTC GILLY PURE CPU EXACT WORKER SHADOW').replace(PARENT,MARKER)
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=str(ROOT),text=True).strip();h=hashlib.sha256();files=sorted((ROOT/'Source/AERISFlightControl').rglob('*.cs'))+[CSPROJ]
for p in files:
    if p==VERSION:continue
    h.update(str(p.relative_to(ROOT)).encode());h.update(b'\0');h.update(p.read_bytes());h.update(b'\0')
version=re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";','internal const string SourceGitSha = "'+head+'";',version)
version=re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";','internal const string SourceTreeSha256 = "'+h.hexdigest()+'";',version)
VERSION.write_text(version)
print('PASS apply R032 Gilly pure CPU exact worker shadow')
print('head='+head)
