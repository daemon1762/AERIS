using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS33 R036: first common pure-procedural CPU-exact worker framework.
    // Main thread snapshots runtime PQS truth and primitive state. Worker consumes only copied
    // scalars/arrays. Unsupported contributor types remain fail-closed and are never approximated.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR036PtcCommonPureCpuExactFormulaClosureObserver : MonoBehaviour
    {
        internal const string CandidateMarker =
            "AERIS33_REV3_5_R036_PTC_COMMON_PURE_CPU_EXACT_FORMULA_CLOSURE_SHADOW";
        const double PrimitiveTolerance = 1e-12;
        const double TerrainToleranceMeters = 1e-8;
        const int MaxIlInstructions = 4096;
        static readonly string[] TargetBodies = new string[] { "Minmus", "Ike", "Gilly", "Pol" };
        static readonly Dictionary<short, OpCode> OneByte = BuildOpcodeMap(false);
        static readonly Dictionary<short, OpCode> TwoByte = BuildOpcodeMap(true);

        enum StepKind
        {
            Unsupported = 0,
            SimplexAbsolute = 1,
            RidgedHeight = 2,
            SimplexSigned = 3,
            HeightOffset = 4
        }

        struct Point3
        {
            internal double X, Y, Z;
            internal Point3(double x, double y, double z) { X=x; Y=y; Z=z; }
        }

        sealed class StepSnapshot
        {
            internal int Index;
            internal string TypeName;
            internal StepKind Kind;
            internal double Deformity;
            internal double Offset;
            internal int[] Perm;
            internal int[] Grad3;
            internal double[] RandomVectors;
            internal double[] SpectralWeights;
            internal double Frequency;
            internal int Octaves;
            internal double Persistence;
            internal double Lacunarity;
            internal int Seed;
            internal int Quality;
            internal double[] NativePrimitive;
        }

        sealed class BodySnapshot
        {
            internal string Name;
            internal double Radius;
            internal List<StepSnapshot> Steps = new List<StepSnapshot>();
            internal bool LandControlInert;
            internal bool WorkerReady;
            internal string PendingReason = string.Empty;
            internal Point3[] PrimitivePoints;
            internal Point3[] TerrainPoints;
            internal double[] PqsTruthMeters;
        }

        sealed class Snapshot
        {
            internal List<BodySnapshot> Bodies = new List<BodySnapshot>();
        }

        sealed class BodyResult
        {
            internal string Name;
            internal bool Evaluated;
            internal double[][] WorkerPrimitive;
            internal double[] TerrainMeters;
            internal string Error;
        }

        sealed class WorkerResult
        {
            internal int ThreadId;
            internal List<BodyResult> Bodies = new List<BodyResult>();
            internal string Error;
        }

        sealed class IlInstruction
        {
            internal int Index;
            internal int Offset;
            internal OpCode Op;
            internal string Operand;
            internal bool VertHeightWrite;
        }

        int mainThreadId;
        bool started;
        bool reported;
        float nextAttempt;
        Snapshot snapshot;
        volatile WorkerResult completed;

        void Update()
        {
            if (reported) return;
            if (!started)
            {
                if (Time.realtimeSinceStartup < nextAttempt) return;
                nextAttempt = Time.realtimeSinceStartup + 1f;
                if (!AERISTerrainTileSystem.GameDataHashReady) return;
                if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
                TryStart();
                return;
            }
            WorkerResult r = completed;
            if (r == null) return;
            reported = true;
            Report(r);
        }

        void TryStart()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            try
            {
                Snapshot s = CaptureSnapshot();
                snapshot = s;
                started = true;
                int ready = 0;
                for (int i=0;i<s.Bodies.Count;i++) if (s.Bodies[i].WorkerReady) ready++;
                AERISLogger.Info("[R036][COMMON_CPU] event=SNAPSHOT_COMPLETE; bodies="+s.Bodies.Count+
                    "; worker_ready_bodies="+ready+"; pending_bodies="+(s.Bodies.Count-ready)+
                    "; snapshot_payload=PRIMITIVES_ONLY; runtime_object_invocation_thread=MAIN_THREAD_ONLY"+
                    "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; db_write=false"+
                    "; producer_switch=false; gpu=false; authority=PQS");
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    WorkerResult wr = new WorkerResult();
                    wr.ThreadId = Thread.CurrentThread.ManagedThreadId;
                    try { EvaluateWorker((Snapshot)state, wr); }
                    catch (Exception ex) { wr.Error = ex.GetType().FullName+":"+ex.Message; }
                    Thread.MemoryBarrier();
                    completed = wr;
                }, s);
            }
            catch (Exception ex)
            {
                started = true;
                reported = true;
                AERISLogger.Error("[R036][COMMON_CPU_FAIL] stage=SNAPSHOT; error="+Safe(ex.GetType().Name+":"+ex.Message)+
                    "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_invokes_runtime_object=false"+
                    "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
            }
        }

        Snapshot CaptureSnapshot()
        {
            Snapshot s = new Snapshot();
            for (int bi=0; bi<TargetBodies.Length; bi++)
            {
                string name = TargetBodies[bi];
                CelestialBody body = FindBody(name);
                if (body == null || body.pqsController == null)
                    throw new InvalidOperationException(name+" body/PQS missing");
                BodySnapshot bs = new BodySnapshot();
                bs.Name = name;
                bs.Radius = body.Radius;
                bs.PrimitivePoints = BuildPrimitivePoints();
                bs.TerrainPoints = BuildTerrainPoints();
                bs.LandControlInert = true;

                IEnumerable mods = ReadMember(body.pqsController, "mods") as IEnumerable;
                if (mods == null) throw new InvalidOperationException(name+" PQS mods missing");
                int stepIndex = 0;
                int heightCallbacks = 0;
                List<string> pending = new List<string>();
                foreach (object mod in mods)
                {
                    if (mod == null) continue;
                    Type mt = mod.GetType();
                    MethodInfo h = mt.GetMethod("OnVertexBuildHeight",
                        BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                    if (h == null) continue;
                    bool enabled = ReadBoolOrDefault(mod, "enabled", true);
                    if (!enabled) continue;
                    heightCallbacks++;
                    string tn = mt.FullName ?? mt.Name;

                    if (tn == "PQSLandControl")
                    {
                        bool inert = LandControlGeometryInert(mod);
                        bs.LandControlInert = bs.LandControlInert && inert;
                        if (!inert) pending.Add("PQSLandControl:GEOMETRY_ACTIVE_OR_UNKNOWN");
                        AERISLogger.Info("[R036][STEP] body="+name+"; index="+stepIndex+
                            "; type="+tn+"; adapter=LANDCONTROL_INERT_GUARD; supported="+inert+
                            "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; authority=PQS");
                        stepIndex++;
                        continue;
                    }

                    StepSnapshot st = CaptureStep(mod, stepIndex, bs.PrimitivePoints);
                    bs.Steps.Add(st);
                    bool supported = st.Kind != StepKind.Unsupported;
                    if (!supported) pending.Add(tn+":FORMULA_CLOSURE_PENDING");
                    AERISLogger.Info("[R036][STEP] body="+name+"; index="+stepIndex+
                        "; type="+tn+"; adapter="+st.Kind.ToString().ToUpperInvariant()+
                        "; supported="+supported+"; runtime_object_invocation_thread=MAIN_THREAD_ONLY; authority=PQS");
                    if (!supported || tn=="PQSMod_VertexSimplexHeight")
                        LogIlClosure(mt, h, tn);
                    stepIndex++;
                }

                bs.WorkerReady = pending.Count == 0 && bs.LandControlInert;
                bs.PendingReason = pending.Count == 0 ? "-" : string.Join(",", pending.ToArray());
                bs.PqsTruthMeters = new double[bs.TerrainPoints.Length];
                MethodInfo surface = FindVector3dMethod(body.pqsController.GetType(), "GetSurfaceHeight");
                double radius = ReadDoubleOrDefault(body.pqsController, "radius", body.Radius);
                for (int i=0;i<bs.TerrainPoints.Length;i++)
                {
                    Point3 p=bs.TerrainPoints[i];
                    object raw=surface.Invoke(body.pqsController,new object[]{new Vector3d(p.X,p.Y,p.Z)});
                    bs.PqsTruthMeters[i]=Convert.ToDouble(raw,CultureInfo.InvariantCulture)-radius;
                }
                AERISLogger.Info("[R036][BODY] body="+name+"; height_callbacks="+heightCallbacks+
                    "; core_steps="+bs.Steps.Count+"; landcontrol_inert="+bs.LandControlInert+
                    "; worker_ready="+bs.WorkerReady+"; pending="+Safe(bs.PendingReason)+
                    "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; authority=PQS");
                s.Bodies.Add(bs);
            }
            return s;
        }

        StepSnapshot CaptureStep(object mod, int index, Point3[] primitivePoints)
        {
            StepSnapshot st = new StepSnapshot();
            st.Index = index;
            st.TypeName = mod.GetType().FullName ?? mod.GetType().Name;
            st.Kind = StepKind.Unsupported;
            if (st.TypeName=="PQSMod_VertexSimplexHeightAbsolute" ||
                st.TypeName=="PQSMod_VertexSimplexHeight")
            {
                object simplex=ReadMember(mod,"simplex");
                if (simplex==null) throw new InvalidOperationException(st.TypeName+" simplex missing");
                st.Perm=CopyIntArray(ReadMember(simplex,"perm"),st.TypeName+".perm");
                FieldInfo grad=simplex.GetType().GetField("grad3",
                    BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                if (grad==null) throw new InvalidOperationException("Simplex.grad3 missing");
                st.Grad3=CopyIntArray(grad.GetValue(null),"Simplex.grad3");
                st.Frequency=ReadDouble(simplex,"frequency");
                st.Octaves=ReadInt(simplex,"octaves");
                st.Persistence=ReadDouble(simplex,"persistence");
                st.Deformity=ReadDouble(mod,"deformity");
                st.Kind=st.TypeName=="PQSMod_VertexSimplexHeightAbsolute" ?
                    StepKind.SimplexAbsolute : StepKind.SimplexSigned;
                st.NativePrimitive=CaptureNativePrimitive(simplex,"noise",primitivePoints);
                ValidateSimplex(st);
            }
            else if (st.TypeName=="PQSMod_VertexHeightNoise")
            {
                object ridged=ReadMember(mod,"noiseMap");
                if (ridged==null) throw new InvalidOperationException("VertexHeightNoise.noiseMap missing");
                Assembly lib=ridged.GetType().Assembly;
                Type basis=lib.GetType("LibNoise.GradientNoiseBasis",false);
                if (basis==null) throw new InvalidOperationException("LibNoise.GradientNoiseBasis missing");
                FieldInfo vectors=basis.GetField("RandomVectors",
                    BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                if (vectors==null) throw new InvalidOperationException("RandomVectors missing");
                st.RandomVectors=CopyDoubleArray(vectors.GetValue(null),"RandomVectors");
                st.SpectralWeights=CopyDoubleArray(ReadMember(ridged,"SpectralWeights"),"SpectralWeights");
                st.Frequency=ReadDouble(ridged,"Frequency");
                st.Lacunarity=ReadDouble(ridged,"Lacunarity");
                st.Octaves=ReadInt(ridged,"OctaveCount");
                st.Seed=ReadInt(ridged,"Seed");
                st.Quality=ReadInt(ridged,"NoiseQuality");
                st.Deformity=ReadDouble(mod,"deformity");
                st.Kind=StepKind.RidgedHeight;
                st.NativePrimitive=CaptureNativePrimitive(ridged,"GetValue",primitivePoints);
                ValidateRidged(st);
            }
            else if (st.TypeName=="PQSMod_VertexHeightOffset")
            {
                st.Offset=ReadDouble(mod,"offset");
                st.Kind=StepKind.HeightOffset;
                st.NativePrimitive=new double[0];
            }
            return st;
        }

        static void ValidateSimplex(StepSnapshot st)
        {
            if (st.Perm==null || st.Perm.Length!=512)
                throw new InvalidOperationException(st.TypeName+" unexpected perm length");
            if (st.Grad3==null || st.Grad3.Length!=36)
                throw new InvalidOperationException(st.TypeName+" unexpected grad3 length");
        }

        static void ValidateRidged(StepSnapshot st)
        {
            if (st.RandomVectors==null || st.RandomVectors.Length!=1024)
                throw new InvalidOperationException(st.TypeName+" unexpected RandomVectors length");
            if (st.SpectralWeights==null || st.SpectralWeights.Length<Math.Max(30,st.Octaves))
                throw new InvalidOperationException(st.TypeName+" unexpected SpectralWeights length");
        }

        static bool LandControlGeometryInert(object land)
        {
            if (ReadBoolOrDefault(land,"useHeightMap",false)) return false;
            Array classes=ReadMember(land,"landClasses") as Array;
            if (classes==null) return false;
            for (int i=0;i<classes.Length;i++)
            {
                object lc=classes.GetValue(i);
                if (lc==null) return false;
                double min=ReadDouble(lc,"minimumRealHeight");
                double real=ReadDouble(lc,"alterRealHeight");
                if (min!=0.0 || real!=0.0) return false;
            }
            return true;
        }

        static void EvaluateWorker(Snapshot s, WorkerResult wr)
        {
            for (int bi=0;bi<s.Bodies.Count;bi++)
            {
                BodySnapshot bs=s.Bodies[bi];
                BodyResult br=new BodyResult();
                br.Name=bs.Name;
                br.Evaluated=bs.WorkerReady;
                if (!bs.WorkerReady)
                {
                    wr.Bodies.Add(br);
                    continue;
                }
                try
                {
                    br.WorkerPrimitive=new double[bs.Steps.Count][];
                    for (int si=0;si<bs.Steps.Count;si++)
                    {
                        StepSnapshot st=bs.Steps[si];
                        if (st.Kind==StepKind.SimplexAbsolute || st.Kind==StepKind.SimplexSigned)
                        {
                            br.WorkerPrimitive[si]=new double[bs.PrimitivePoints.Length];
                            for (int pi=0;pi<bs.PrimitivePoints.Length;pi++)
                            {
                                Point3 p=bs.PrimitivePoints[pi];
                                br.WorkerPrimitive[si][pi]=SimplexNoise(st,p.X,p.Y,p.Z);
                            }
                        }
                        else if (st.Kind==StepKind.RidgedHeight)
                        {
                            br.WorkerPrimitive[si]=new double[bs.PrimitivePoints.Length];
                            for (int pi=0;pi<bs.PrimitivePoints.Length;pi++)
                            {
                                Point3 p=bs.PrimitivePoints[pi];
                                br.WorkerPrimitive[si][pi]=RidgedValue(st,p.X,p.Y,p.Z);
                            }
                        }
                        else br.WorkerPrimitive[si]=new double[0];
                    }
                    br.TerrainMeters=new double[bs.TerrainPoints.Length];
                    for (int pi=0;pi<bs.TerrainPoints.Length;pi++)
                    {
                        Point3 p=bs.TerrainPoints[pi];
                        double h=0.0;
                        for (int si=0;si<bs.Steps.Count;si++)
                            h=ApplyStep(bs.Steps[si],p,h);
                        br.TerrainMeters[pi]=h;
                    }
                }
                catch(Exception ex)
                {
                    br.Error=ex.GetType().FullName+":"+ex.Message;
                }
                wr.Bodies.Add(br);
            }
        }

        static double ApplyStep(StepSnapshot st, Point3 p, double h)
        {
            if (st.Kind==StepKind.SimplexAbsolute)
                return h+(SimplexNoise(st,p.X,p.Y,p.Z)+1.0)*0.5*st.Deformity;
            if (st.Kind==StepKind.RidgedHeight)
                return h+RidgedValue(st,p.X,p.Y,p.Z)*st.Deformity;
            if (st.Kind==StepKind.SimplexSigned)
                return h+SimplexNoise(st,p.X,p.Y,p.Z)*st.Deformity;
            if (st.Kind==StepKind.HeightOffset)
                return h+st.Offset;
            throw new InvalidOperationException("unsupported worker step "+st.TypeName);
        }

        void Report(WorkerResult wr)
        {
            if (!string.IsNullOrEmpty(wr.Error))
            {
                AERISLogger.Error("[R036][COMMON_CPU_FAIL] stage=WORKER; error="+Safe(wr.Error)+
                    "; worker_thread_id="+wr.ThreadId+"; worker_invokes_runtime_object=false"+
                    "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
                return;
            }
            Snapshot s=snapshot;
            if (s==null)
            {
                AERISLogger.Error("[R036][COMMON_CPU_FAIL] stage=REPORT; error=SNAPSHOT_MISSING; authority=PQS");
                return;
            }
            int ready=0,pending=0,bodyFailures=0,totalPrimitiveFailures=0,totalTerrainFailures=0;
            double maxPrimitive=0.0,maxTerrain=0.0;
            for (int bi=0;bi<s.Bodies.Count;bi++)
            {
                BodySnapshot bs=s.Bodies[bi];
                BodyResult br=FindResult(wr,bs.Name);
                if (!bs.WorkerReady)
                {
                    pending++;
                    AERISLogger.Info("[R036][BODY_RESULT] body="+bs.Name+
                        "; evaluated=false; primitive_failures=0; terrain_failures=0; max_primitive_abs_error=0"+
                        "; max_terrain_abs_error_m=0; pending="+Safe(bs.PendingReason)+
                        "; worker_invokes_runtime_object=false; authority=PQS");
                    continue;
                }
                ready++;
                if (br==null || !string.IsNullOrEmpty(br.Error) || !br.Evaluated)
                {
                    bodyFailures++;
                    AERISLogger.Error("[R036][BODY_RESULT] body="+bs.Name+
                        "; evaluated=false; error="+Safe(br==null?"RESULT_MISSING":br.Error)+
                        "; worker_invokes_runtime_object=false; authority=PQS");
                    continue;
                }
                int pf=0,tf=0;
                double bp=0.0,bt=0.0;
                for (int si=0;si<bs.Steps.Count;si++)
                {
                    StepSnapshot st=bs.Steps[si];
                    if (st.NativePrimitive==null || st.NativePrimitive.Length==0) continue;
                    double[] w=br.WorkerPrimitive[si];
                    for (int pi=0;pi<st.NativePrimitive.Length;pi++)
                    {
                        double d=Math.Abs(st.NativePrimitive[pi]-w[pi]);
                        if (d>bp) bp=d;
                        if (d>PrimitiveTolerance) pf++;
                    }
                }
                for (int pi=0;pi<bs.PqsTruthMeters.Length;pi++)
                {
                    double d=Math.Abs(bs.PqsTruthMeters[pi]-br.TerrainMeters[pi]);
                    if (d>bt) bt=d;
                    if (d>TerrainToleranceMeters) tf++;
                }
                if (pf!=0 || tf!=0) bodyFailures++;
                totalPrimitiveFailures+=pf;
                totalTerrainFailures+=tf;
                if (bp>maxPrimitive) maxPrimitive=bp;
                if (bt>maxTerrain) maxTerrain=bt;
                AERISLogger.Info("[R036][BODY_RESULT] body="+bs.Name+
                    "; evaluated=true; primitive_failures="+pf+"; terrain_failures="+tf+
                    "; max_primitive_abs_error="+F(bp)+"; max_terrain_abs_error_m="+F(bt)+
                    "; terrain_points="+bs.PqsTruthMeters.Length+
                    "; worker_invokes_runtime_object=false; authority=PQS");
            }
            bool offMain=wr.ThreadId!=0 && wr.ThreadId!=mainThreadId;
            AERISLogger.Info("[R036][COMMON_CPU] event=FORMULA_CLOSURE_WORKER_COMPLETE"+
                "; bodies="+s.Bodies.Count+"; worker_ready_bodies="+ready+"; pending_bodies="+pending+
                "; body_failures="+bodyFailures+"; primitive_failures="+totalPrimitiveFailures+
                "; terrain_failures="+totalTerrainFailures+"; max_primitive_abs_error="+F(maxPrimitive)+
                "; max_terrain_abs_error_m="+F(maxTerrain)+
                "; primitive_tolerance="+F(PrimitiveTolerance)+"; terrain_tolerance_m="+F(TerrainToleranceMeters)+
                "; main_thread_id="+mainThreadId+"; worker_thread_id="+wr.ThreadId+"; worker_off_main="+offMain+
                "; snapshot_payload=PRIMITIVES_ONLY; runtime_object_invocation_thread=MAIN_THREAD_ONLY"+
                "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; db_write=false"+
                "; producer_switch=false; gpu=false; authority=PQS");
        }

        static BodyResult FindResult(WorkerResult wr,string name)
        {
            for(int i=0;i<wr.Bodies.Count;i++)
                if(string.Equals(wr.Bodies[i].Name,name,StringComparison.Ordinal)) return wr.Bodies[i];
            return null;
        }

        static double[] CaptureNativePrimitive(object noise,string method,Point3[] points)
        {
            MethodInfo m=FindTripleDoubleMethod(noise.GetType(),method);
            double[] d=new double[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                Point3 p=points[i];
                d[i]=Convert.ToDouble(m.Invoke(noise,new object[]{p.X,p.Y,p.Z}),CultureInfo.InvariantCulture);
            }
            return d;
        }

        static Point3[] BuildPrimitivePoints()
        {
            return new Point3[] {
                new Point3(0,0,0), new Point3(0.1,0.2,0.3),
                new Point3(-0.25,0.5,-0.75), new Point3(1,2,3),
                new Point3(12.345,-67.89,0.125),
                new Point3(-Math.PI,Math.E,0.577215664901533)
            };
        }

        static Point3[] BuildTerrainPoints()
        {
            Point3[] p=new Point3[] {
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

        static double SimplexNoise(StepSnapshot s,double x,double y,double z)
        {
            double result=0.0,max=0.0,frequency=s.Frequency,amplitude=1.0;
            int itr=0;
            while(itr<s.Octaves)
            {
                result+=SimplexValue(s,x*frequency,y*frequency,z*frequency)*amplitude;
                frequency*=2.0; max+=amplitude; amplitude*=s.Persistence; itr++;
            }
            return result/max;
        }

        static double SimplexValue(StepSnapshot s,double x,double y,double z)
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
            return 32.0*(Corner(s,gi0,x0,y0,z0)+Corner(s,gi1,x1,y1,z1)+
                Corner(s,gi2,x2,y2,z2)+Corner(s,gi3,x3,y3,z3));
        }

        static double Corner(StepSnapshot s,int gi,double x,double y,double z)
        {
            double t=0.6-x*x-y*y-z*z;
            if(t<0.0)return 0.0;
            t*=t;
            int o=gi*3;
            return t*t*(s.Grad3[o]*x+s.Grad3[o+1]*y+s.Grad3[o+2]*z);
        }

        static int FastFloor(double x){return x<=0.0?(int)x-1:(int)x;}

        static double RidgedValue(StepSnapshot s,double x,double y,double z)
        {
            x*=s.Frequency;y*=s.Frequency;z*=s.Frequency;
            double signal=0.0,value=0.0,weight=1.0,offset=1.0,gain=2.0;
            for(int octave=0;octave<s.Octaves;octave++)
            {
                int seed=(s.Seed+octave)&0x7fffffff;
                signal=GradientCoherentNoise(s,x,y,z,seed,s.Quality);
                signal=Math.Abs(signal); signal=offset-signal; signal*=signal; signal*=weight;
                weight=signal*gain; if(weight>1.0)weight=1.0; if(weight<0.0)weight=0.0;
                value+=signal*s.SpectralWeights[octave];
                x*=s.Lacunarity;y*=s.Lacunarity;z*=s.Lacunarity;
            }
            return value*1.25-1.0;
        }

        static double GradientCoherentNoise(StepSnapshot s,double x,double y,double z,int seed,int quality)
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

        static double GradientNoise(StepSnapshot s,double fx,double fy,double fz,int ix,int iy,int iz,long seed)
        {
            int spatial=unchecked(1619*ix+31337*iy+6971*iz);
            long vectorIndex=(unchecked((long)spatial)+1013L*seed)&0xffffffffL;
            vectorIndex^=(vectorIndex>>8); vectorIndex&=255L;
            int o=(int)(vectorIndex<<2);
            double dx=fx-ix,dy=fy-iy,dz=fz-iz;
            return (s.RandomVectors[o]*dx+s.RandomVectors[o+1]*dy+s.RandomVectors[o+2]*dz)*2.12;
        }

        static double LinearInterpolate(double a,double b,double p){return (1.0-p)*a+p*b;}
        static double SCurve3(double a){return a*a*(3.0-2.0*a);}
        static double SCurve5(double a){double a3=a*a*a,a4=a3*a,a5=a4*a;return 6.0*a5-15.0*a4+10.0*a3;}

        static void LogIlClosure(Type type, MethodInfo method, string label)
        {
            if(method==null)
            {
                AERISLogger.Error("[R036][IL_FAIL] type="+Safe(label)+"; error=METHOD_MISSING; authority=PQS");
                return;
            }
            try
            {
                MethodBody body=method.GetMethodBody();
                if(body==null)
                {
                    AERISLogger.Error("[R036][IL_FAIL] type="+Safe(label)+"; error=METHOD_BODY_MISSING; authority=PQS");
                    return;
                }
                byte[] il=body.GetILAsByteArray();
                if(il==null) il=new byte[0];
                string sha=Sha256(il);
                List<IlInstruction> decoded=DecodeIl(method,il);
                int writes=0;
                for(int i=0;i<decoded.Count;i++) if(decoded[i].VertHeightWrite) writes++;
                AERISLogger.Info("[R036][IL] type="+Safe(label)+"; code_size="+il.Length+
                    "; instructions="+decoded.Count+"; direct_vertHeight_writes="+writes+
                    "; il_sha256="+sha+"; authority=PQS");
                for(int i=0;i<decoded.Count;i++)
                {
                    IlInstruction d=decoded[i];
                    AERISLogger.Info("[R036][IL_INSN] type="+Safe(label)+"; index="+d.Index+
                        "; offset="+d.Offset+"; opcode="+Safe(d.Op.Name)+"; operand="+Safe(d.Operand)+
                        "; vertHeight_write="+d.VertHeightWrite+"; authority=PQS");
                }
            }
            catch(Exception ex)
            {
                AERISLogger.Error("[R036][IL_FAIL] type="+Safe(label)+"; error="+
                    Safe(ex.GetType().Name+":"+ex.Message)+"; authority=PQS");
            }
        }

        static List<IlInstruction> DecodeIl(MethodBase method, byte[] il)
        {
            List<IlInstruction> outp=new List<IlInstruction>();
            Module module=method.Module;
            int pos=0;
            while(pos<il.Length)
            {
                if(outp.Count>=MaxIlInstructions) throw new InvalidOperationException("IL instruction cap exceeded");
                int offset=pos;
                OpCode op;
                byte first=il[pos++];
                if(first==0xfe)
                {
                    if(pos>=il.Length) throw new InvalidOperationException("truncated two-byte opcode");
                    short key=unchecked((short)(0xfe00|il[pos++]));
                    if(!TwoByte.TryGetValue(key,out op)) throw new InvalidOperationException("unknown opcode "+key);
                }
                else
                {
                    short key=(short)first;
                    if(!OneByte.TryGetValue(key,out op)) throw new InvalidOperationException("unknown opcode "+key);
                }
                int start=pos;
                int size=OperandSize(op.OperandType,il,pos);
                if(size<0 || pos+size>il.Length) throw new InvalidOperationException("invalid operand size");
                bool write=false;
                string operand=DecodeOperand(method,module,op,il,start,size,ref write);
                IlInstruction d=new IlInstruction();
                d.Index=outp.Count;d.Offset=offset;d.Op=op;d.Operand=operand;d.VertHeightWrite=write;
                outp.Add(d);
                pos+=size;
            }
            return outp;
        }

        static string DecodeOperand(MethodBase method,Module module,OpCode op,byte[] il,int start,int size,ref bool write)
        {
            switch(op.OperandType)
            {
                case OperandType.InlineNone: return "-";
                case OperandType.ShortInlineI: return unchecked((sbyte)il[start]).ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineI: return BitConverter.ToInt32(il,start).ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineI8: return BitConverter.ToInt64(il,start).ToString(CultureInfo.InvariantCulture);
                case OperandType.ShortInlineR: return BitConverter.ToSingle(il,start).ToString("R",CultureInfo.InvariantCulture);
                case OperandType.InlineR: return BitConverter.ToDouble(il,start).ToString("R",CultureInfo.InvariantCulture);
                case OperandType.ShortInlineVar: return "var:"+il[start];
                case OperandType.InlineVar: return "var:"+BitConverter.ToUInt16(il,start);
                case OperandType.ShortInlineBrTarget:
                    return "target:"+(start+size+unchecked((sbyte)il[start])).ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineBrTarget:
                    return "target:"+(start+size+BitConverter.ToInt32(il,start)).ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineSwitch:
                {
                    int count=BitConverter.ToInt32(il,start);
                    int baseOffset=start+4+count*4;
                    StringBuilder sb=new StringBuilder("targets:");
                    for(int i=0;i<count;i++){if(i!=0)sb.Append(',');sb.Append(baseOffset+BitConverter.ToInt32(il,start+4+i*4));}
                    return sb.ToString();
                }
                case OperandType.InlineString: return "string:"+module.ResolveString(BitConverter.ToInt32(il,start));
                case OperandType.InlineField:
                {
                    FieldInfo f=module.ResolveField(BitConverter.ToInt32(il,start),
                        method.DeclaringType==null?null:method.DeclaringType.GetGenericArguments(),
                        method.IsGenericMethod?method.GetGenericArguments():null);
                    if(f==null)return "field:null";
                    if((op==OpCodes.Stfld||op==OpCodes.Stsfld)&&f.Name=="vertHeight")write=true;
                    return "field:"+TypeLabel(f.DeclaringType)+"."+f.Name+":"+TypeLabel(f.FieldType);
                }
                case OperandType.InlineMethod:
                {
                    MethodBase m=module.ResolveMethod(BitConverter.ToInt32(il,start),
                        method.DeclaringType==null?null:method.DeclaringType.GetGenericArguments(),
                        method.IsGenericMethod?method.GetGenericArguments():null);
                    return "method:"+(m==null?"null":TypeLabel(m.DeclaringType)+"."+m.Name);
                }
                case OperandType.InlineType:
                {
                    Type t=module.ResolveType(BitConverter.ToInt32(il,start),
                        method.DeclaringType==null?null:method.DeclaringType.GetGenericArguments(),
                        method.IsGenericMethod?method.GetGenericArguments():null);
                    return "type:"+TypeLabel(t);
                }
                case OperandType.InlineTok:
                {
                    MemberInfo m=module.ResolveMember(BitConverter.ToInt32(il,start),
                        method.DeclaringType==null?null:method.DeclaringType.GetGenericArguments(),
                        method.IsGenericMethod?method.GetGenericArguments():null);
                    return "member:"+(m==null?"null":TypeLabel(m.DeclaringType)+"."+m.Name);
                }
                case OperandType.InlineSig: return "sig_token:"+BitConverter.ToInt32(il,start);
                default: return "raw_size:"+size;
            }
        }

        static int OperandSize(OperandType type,byte[] il,int pos)
        {
            switch(type)
            {
                case OperandType.InlineNone:return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:return 1;
                case OperandType.InlineVar:return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:return 8;
                case OperandType.InlineSwitch:
                    if(pos+4>il.Length)return -1;
                    int count=BitConverter.ToInt32(il,pos);
                    if(count<0 || count>4096)return -1;
                    return 4+count*4;
                default:return -1;
            }
        }

        static Dictionary<short,OpCode> BuildOpcodeMap(bool two)
        {
            Dictionary<short,OpCode> map=new Dictionary<short,OpCode>();
            FieldInfo[] f=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static);
            for(int i=0;i<f.Length;i++)
            {
                object v=f[i].GetValue(null);if(!(v is OpCode))continue;
                OpCode op=(OpCode)v;bool isTwo=(op.Value&0xff00)==0xfe00;
                if(isTwo==two)map[op.Value]=op;
            }
            return map;
        }

        static string Sha256(byte[] data)
        {
            using(SHA256 sha=SHA256.Create())
            {
                byte[] h=sha.ComputeHash(data);
                StringBuilder sb=new StringBuilder(h.Length*2);
                for(int i=0;i<h.Length;i++)sb.Append(h[i].ToString("x2",CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static CelestialBody FindBody(string name)
        {
            for(int i=0;i<FlightGlobals.Bodies.Count;i++)
            {
                CelestialBody b=FlightGlobals.Bodies[i];
                if(b!=null && string.Equals(b.bodyName,name,StringComparison.Ordinal))return b;
            }
            return null;
        }

        static MethodInfo FindTripleDoubleMethod(Type t,string name)
        {
            MethodInfo m=t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
                null,new Type[]{typeof(double),typeof(double),typeof(double)},null);
            if(m==null)throw new MissingMethodException(t.FullName,name);
            return m;
        }

        static MethodInfo FindVector3dMethod(Type t,string name)
        {
            MethodInfo m=t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
                null,new Type[]{typeof(Vector3d)},null);
            if(m==null)throw new MissingMethodException(t.FullName,name);
            return m;
        }

        static object ReadMember(object target,string name)
        {
            if(target==null)return null;
            Type t=target.GetType();
            while(t!=null)
            {
                FieldInfo f=t.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(f!=null)return f.GetValue(f.IsStatic?null:target);
                PropertyInfo p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(p!=null && p.CanRead && p.GetIndexParameters().Length==0)
                {
                    MethodInfo g=p.GetGetMethod(true);
                    return p.GetValue(g!=null&&g.IsStatic?null:target,null);
                }
                t=t.BaseType;
            }
            return null;
        }

        static bool ReadBoolOrDefault(object target,string name,bool fallback)
        {
            object v=ReadMember(target,name);
            return v==null?fallback:Convert.ToBoolean(v,CultureInfo.InvariantCulture);
        }
        static double ReadDouble(object target,string name)
        {
            object v=ReadMember(target,name);
            if(v==null)throw new MissingMemberException(target.GetType().FullName,name);
            return Convert.ToDouble(v,CultureInfo.InvariantCulture);
        }
        static double ReadDoubleOrDefault(object target,string name,double fallback)
        {
            object v=ReadMember(target,name);
            return v==null?fallback:Convert.ToDouble(v,CultureInfo.InvariantCulture);
        }
        static int ReadInt(object target,string name)
        {
            object v=ReadMember(target,name);
            if(v==null)throw new MissingMemberException(target.GetType().FullName,name);
            return Convert.ToInt32(v,CultureInfo.InvariantCulture);
        }
        static int[] CopyIntArray(object value,string name)
        {
            Array a=value as Array;if(a==null)throw new InvalidOperationException(name+" missing");
            List<int>d=new List<int>();FlattenInt(a,d);return d.ToArray();
        }
        static void FlattenInt(Array a,List<int>d)
        {
            for(int i=0;i<a.Length;i++)
            {
                object v=a.GetValue(i);Array n=v as Array;
                if(n!=null)FlattenInt(n,d);else d.Add(Convert.ToInt32(v,CultureInfo.InvariantCulture));
            }
        }
        static double[] CopyDoubleArray(object value,string name)
        {
            Array a=value as Array;if(a==null)throw new InvalidOperationException(name+" missing");
            double[] d=new double[a.Length];
            for(int i=0;i<a.Length;i++)d[i]=Convert.ToDouble(a.GetValue(i),CultureInfo.InvariantCulture);
            return d;
        }
        static string TypeLabel(Type t){return t==null?"null":(t.FullName??t.Name);}
        static string F(double v){return v.ToString("R",CultureInfo.InvariantCulture);}
        static string Safe(string s)
        {
            return string.IsNullOrEmpty(s)?"-":s.Replace('\n',' ').Replace('\r',' ').Replace(';',',').Replace('|','/');
        }
    }
}
