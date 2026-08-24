using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS34 R039:
    // Exact IL/control-flow capture for stock Minmus PQSMod_VertexPlanet.
    //
    // Shadow-only observer.
    // Runtime KSP/PQS objects are inspected only on the main thread.
    // No worker invokes runtime objects.
    // No terrain DB mutation, producer switch, GPU path, or certification.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR039PtcMinmusVertexPlanetIlBasicBlockObserver : MonoBehaviour
    {
        internal const string CandidateMarker =
            "AERIS34_REV3_5_R039_MINMUS_VERTEXPLANET_IL_BASIC_BLOCK_SHADOW";

        const string ExpectedMainIlSha =
            "513748e2fdcc9eae0ed4958840f485ad5cd6eea4efb078e81ce5ae7bd400f687";

        const int ExpectedIlBytes = 897;
        const int ExpectedInstructionCount = 305;

        static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();

        bool done;
        float nextAttempt;

        sealed class Insn
        {
            internal int Index;
            internal int Offset;
            internal int EndOffset;
            internal OpCode Op;
            internal string Operand;
            internal readonly List<int> Targets = new List<int>();
            internal int Block = -1;
            internal bool Leader;
        }

        sealed class Block
        {
            internal int Id;
            internal int First;
            internal int Last;
            internal readonly List<int> Successors = new List<int>();
        }

        void Update()
        {
            if (done) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;

            nextAttempt = Time.realtimeSinceStartup + 1f;

            if (!AERISTerrainTileSystem.GameDataHashReady) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            done = true;

            try
            {
                Audit();
            }
            catch (Exception ex)
            {
                AERISLogger.Error(
                    "[R039][VERTEXPLANET_IL_FAIL] stage=AUDIT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                    "; worker_invokes_runtime_object=false" +
                    "; certification=NO_SHADOW_ONLY" +
                    "; db_write=false; producer_switch=false; gpu=false" +
                    "; authority=PQS");
            }
        }

        void Audit()
        {
            CelestialBody minmus = FindBody("Minmus");

            if (minmus == null || minmus.pqsController == null)
                throw new InvalidOperationException("Minmus body/PQS missing");

            object mod = FindVertexPlanet(minmus.pqsController);

            if (mod == null)
                throw new InvalidOperationException(
                    "Minmus PQSMod_VertexPlanet missing");

            Type type = mod.GetType();
            MethodInfo method = FindExpectedMainMethod(type);

            if (method == null)
                throw new InvalidOperationException(
                    "expected VertexPlanet OnVertexBuildHeight IL missing");

            MethodBody body = method.GetMethodBody();

            if (body == null)
                throw new InvalidOperationException("method body missing");

            byte[] il = body.GetILAsByteArray();

            if (il == null)
                throw new InvalidOperationException("IL missing");

            string sha = Sha256(il);

            if (!string.Equals(
                sha,
                ExpectedMainIlSha,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "main IL SHA mismatch " + sha);
            }

            if (il.Length != ExpectedIlBytes)
            {
                throw new InvalidOperationException(
                    "IL byte count expected=" +
                    ExpectedIlBytes.ToString(CultureInfo.InvariantCulture) +
                    " actual=" +
                    il.Length.ToString(CultureInfo.InvariantCulture));
            }

            List<Insn> insns = Decode(method, il);

            if (insns.Count != ExpectedInstructionCount)
            {
                throw new InvalidOperationException(
                    "instruction count expected=" +
                    ExpectedInstructionCount.ToString(CultureInfo.InvariantCulture) +
                    " actual=" +
                    insns.Count.ToString(CultureInfo.InvariantCulture));
            }

            List<Block> blocks = BuildBlocks(insns);

            AERISLogger.Info(
                "[R039][VERTEXPLANET_IL_METHOD]" +
                " body=Minmus" +
                "; runtime_type=" + Safe(type.FullName ?? type.Name) +
                "; method=" + Safe(Signature(method)) +
                "; metadata_token=0x" +
                method.MetadataToken.ToString(
                    "X8", CultureInfo.InvariantCulture) +
                "; il_bytes=" +
                il.Length.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" +
                insns.Count.ToString(CultureInfo.InvariantCulture) +
                "; basic_blocks=" +
                blocks.Count.ToString(CultureInfo.InvariantCulture) +
                "; max_stack=" +
                body.MaxStackSize.ToString(CultureInfo.InvariantCulture) +
                "; local_count=" +
                body.LocalVariables.Count.ToString(CultureInfo.InvariantCulture) +
                "; exception_clauses=" +
                body.ExceptionHandlingClauses.Count.ToString(
                    CultureInfo.InvariantCulture) +
                "; il_sha256=" + sha +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                "; worker_invokes_runtime_object=false" +
                "; certification=NO_SHADOW_ONLY" +
                "; db_write=false; producer_switch=false; gpu=false" +
                "; authority=PQS");

            for (int i = 0; i < body.LocalVariables.Count; i++)
            {
                LocalVariableInfo local = body.LocalVariables[i];

                AERISLogger.Info(
                    "[R039][VERTEXPLANET_LOCAL]" +
                    " index=" +
                    i.ToString(CultureInfo.InvariantCulture) +
                    "; type=" +
                    Safe(local.LocalType == null
                        ? "NULL"
                        : (local.LocalType.FullName ??
                           local.LocalType.Name)) +
                    "; pinned=" + local.IsPinned +
                    "; authority=IL");
            }

            int vertHeightReads = 0;
            int vertHeightWrites = 0;

            for (int i = 0; i < insns.Count; i++)
            {
                Insn insn = insns[i];

                bool vertHeight =
                    !string.IsNullOrEmpty(insn.Operand) &&
                    insn.Operand.IndexOf(
                        ".vertHeight",
                        StringComparison.OrdinalIgnoreCase) >= 0;

                string access = "NONE";

                if (vertHeight)
                {
                    if (insn.Op == OpCodes.Stfld ||
                        insn.Op == OpCodes.Stsfld)
                    {
                        access = "WRITE";
                        vertHeightWrites++;
                    }
                    else
                    {
                        access = "READ";
                        vertHeightReads++;
                    }
                }

                AERISLogger.Info(
                    "[R039][VERTEXPLANET_IL_INSN]" +
                    " index=" +
                    insn.Index.ToString(CultureInfo.InvariantCulture) +
                    "; block=B" +
                    insn.Block.ToString(
                        "D3", CultureInfo.InvariantCulture) +
                    "; leader=" + insn.Leader +
                    "; offset=IL_" +
                    insn.Offset.ToString(
                        "X4", CultureInfo.InvariantCulture) +
                    "; end=IL_" +
                    insn.EndOffset.ToString(
                        "X4", CultureInfo.InvariantCulture) +
                    "; opcode=" + Safe(insn.Op.Name) +
                    "; operand=" + Safe(insn.Operand) +
                    "; targets=" + Safe(TargetList(insn.Targets)) +
                    "; flow=" + Safe(insn.Op.FlowControl.ToString()) +
                    "; vertHeight_access=" + access +
                    "; authority=IL");
            }

            for (int i = 0; i < blocks.Count; i++)
            {
                Block block = blocks[i];

                Insn first = insns[block.First];
                Insn last = insns[block.Last];

                AERISLogger.Info(
                    "[R039][VERTEXPLANET_BASIC_BLOCK]" +
                    " id=B" +
                    block.Id.ToString(
                        "D3", CultureInfo.InvariantCulture) +
                    "; start=IL_" +
                    first.Offset.ToString(
                        "X4", CultureInfo.InvariantCulture) +
                    "; end=IL_" +
                    last.EndOffset.ToString(
                        "X4", CultureInfo.InvariantCulture) +
                    "; first_index=" +
                    block.First.ToString(CultureInfo.InvariantCulture) +
                    "; last_index=" +
                    block.Last.ToString(CultureInfo.InvariantCulture) +
                    "; instruction_count=" +
                    (block.Last - block.First + 1).ToString(
                        CultureInfo.InvariantCulture) +
                    "; terminator=" + Safe(last.Op.Name) +
                    "; flow=" +
                    Safe(last.Op.FlowControl.ToString()) +
                    "; successors=" +
                    Safe(BlockList(block.Successors)) +
                    "; authority=IL");
            }

            AERISLogger.Info(
                "[R039][VERTEXPLANET_IL_CFG_COMPLETE]" +
                " body=Minmus" +
                "; il_bytes=" +
                il.Length.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" +
                insns.Count.ToString(CultureInfo.InvariantCulture) +
                "; basic_blocks=" +
                blocks.Count.ToString(CultureInfo.InvariantCulture) +
                "; vertHeight_reads=" +
                vertHeightReads.ToString(CultureInfo.InvariantCulture) +
                "; vertHeight_writes=" +
                vertHeightWrites.ToString(CultureInfo.InvariantCulture) +
                "; main_il_sha256=" + sha +
                "; failures=0" +
                "; worker_ready=false" +
                "; pending=PURE_CPU_FORMULA_RECONSTRUCTION" +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                "; worker_invokes_runtime_object=false" +
                "; certification=NO_SHADOW_ONLY" +
                "; db_write=false; producer_switch=false; gpu=false" +
                "; authority=PQS");
        }

        static MethodInfo FindExpectedMainMethod(Type type)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];

                if (method == null ||
                    method.Name != "OnVertexBuildHeight")
                    continue;

                MethodBody body;

                try
                {
                    body = method.GetMethodBody();
                }
                catch
                {
                    continue;
                }

                if (body == null) continue;

                byte[] il = body.GetILAsByteArray();

                if (il == null) continue;

                if (string.Equals(
                    Sha256(il),
                    ExpectedMainIlSha,
                    StringComparison.Ordinal))
                {
                    return method;
                }
            }

            return null;
        }

        static List<Insn> Decode(MethodInfo method, byte[] il)
        {
            var result = new List<Insn>();

            int p = 0;
            int index = 0;

            while (p < il.Length)
            {
                int offset = p;

                ushort key = il[p++];

                if (key == 0xFE)
                {
                    if (p >= il.Length)
                        throw new InvalidOperationException(
                            "truncated two-byte opcode");

                    key = (ushort)(0xFE00 | il[p++]);
                }

                OpCode op;

                if (!Codes.TryGetValue(key, out op))
                {
                    throw new InvalidOperationException(
                        "unknown opcode 0x" +
                        key.ToString(
                            "X4", CultureInfo.InvariantCulture) +
                        " at IL_" +
                        offset.ToString(
                            "X4", CultureInfo.InvariantCulture));
                }

                var insn = new Insn();

                insn.Index = index++;
                insn.Offset = offset;
                insn.Op = op;
                insn.Operand =
                    ReadOperand(
                        method,
                        il,
                        ref p,
                        op,
                        insn.Targets);
                insn.EndOffset = p;

                result.Add(insn);
            }

            return result;
        }

        static string ReadOperand(
            MethodInfo method,
            byte[] il,
            ref int p,
            OpCode op,
            List<int> targets)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    return string.Empty;

                case OperandType.ShortInlineI:
                    return ((sbyte)il[p++]).ToString(
                        CultureInfo.InvariantCulture);

                case OperandType.InlineI:
                {
                    int v = BitConverter.ToInt32(il, p);
                    p += 4;

                    return v.ToString(
                        CultureInfo.InvariantCulture);
                }

                case OperandType.InlineI8:
                {
                    long v = BitConverter.ToInt64(il, p);
                    p += 8;

                    return v.ToString(
                        CultureInfo.InvariantCulture);
                }

                case OperandType.ShortInlineR:
                {
                    float v = BitConverter.ToSingle(il, p);
                    p += 4;

                    return v.ToString(
                        "R", CultureInfo.InvariantCulture);
                }

                case OperandType.InlineR:
                {
                    double v = BitConverter.ToDouble(il, p);
                    p += 8;

                    return v.ToString(
                        "R", CultureInfo.InvariantCulture);
                }

                case OperandType.ShortInlineVar:
                    return il[p++].ToString(
                        CultureInfo.InvariantCulture);

                case OperandType.InlineVar:
                {
                    ushort v = BitConverter.ToUInt16(il, p);
                    p += 2;

                    return v.ToString(
                        CultureInfo.InvariantCulture);
                }

                case OperandType.ShortInlineBrTarget:
                {
                    sbyte d = (sbyte)il[p++];
                    int target = p + d;

                    targets.Add(target);

                    return "IL_" +
                        target.ToString(
                            "X4", CultureInfo.InvariantCulture);
                }

                case OperandType.InlineBrTarget:
                {
                    int d = BitConverter.ToInt32(il, p);
                    p += 4;

                    int target = p + d;
                    targets.Add(target);

                    return "IL_" +
                        target.ToString(
                            "X4", CultureInfo.InvariantCulture);
                }

                case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, p);
                    p += 4;

                    int baseOffset = p + 4 * count;

                    var names = new string[count];

                    for (int i = 0; i < count; i++)
                    {
                        int d = BitConverter.ToInt32(il, p);
                        p += 4;

                        int target = baseOffset + d;

                        targets.Add(target);

                        names[i] =
                            "IL_" +
                            target.ToString(
                                "X4",
                                CultureInfo.InvariantCulture);
                    }

                    return string.Join(",", names);
                }

                case OperandType.InlineString:
                {
                    int token = BitConverter.ToInt32(il, p);
                    p += 4;

                    return Token(method, token, true);
                }

                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                {
                    int token = BitConverter.ToInt32(il, p);
                    p += 4;

                    return Token(method, token, false);
                }

                default:
                    throw new InvalidOperationException(
                        "unsupported operand type " +
                        op.OperandType);
            }
        }

        static List<Block> BuildBlocks(List<Insn> insns)
        {
            if (insns == null || insns.Count == 0)
                throw new InvalidOperationException(
                    "empty instruction stream");

            var offsets = new Dictionary<int, int>();

            for (int i = 0; i < insns.Count; i++)
            {
                offsets[insns[i].Offset] = i;
            }

            var leaders = new HashSet<int>();

            leaders.Add(insns[0].Offset);

            for (int i = 0; i < insns.Count; i++)
            {
                Insn insn = insns[i];

                for (int t = 0; t < insn.Targets.Count; t++)
                {
                    int target = insn.Targets[t];

                    if (!offsets.ContainsKey(target))
                    {
                        throw new InvalidOperationException(
                            "branch target does not point at instruction IL_" +
                            target.ToString(
                                "X4",
                                CultureInfo.InvariantCulture));
                    }

                    leaders.Add(target);
                }

                if (EndsBlock(insn.Op) &&
                    i + 1 < insns.Count)
                {
                    leaders.Add(insns[i + 1].Offset);
                }
            }

            var blocks = new List<Block>();

            Block current = null;

            for (int i = 0; i < insns.Count; i++)
            {
                Insn insn = insns[i];

                if (leaders.Contains(insn.Offset))
                {
                    if (current != null)
                    {
                        current.Last = i - 1;
                        blocks.Add(current);
                    }

                    current = new Block();
                    current.Id = blocks.Count;
                    current.First = i;

                    insn.Leader = true;
                }

                if (current == null)
                    throw new InvalidOperationException(
                        "no basic block for instruction");

                insn.Block = current.Id;
            }

            if (current != null)
            {
                current.Last = insns.Count - 1;
                blocks.Add(current);
            }

            var blockByOffset = new Dictionary<int, int>();

            for (int i = 0; i < blocks.Count; i++)
            {
                blockByOffset[
                    insns[blocks[i].First].Offset] = blocks[i].Id;
            }

            for (int i = 0; i < blocks.Count; i++)
            {
                Block block = blocks[i];
                Insn last = insns[block.Last];

                if (last.Op.FlowControl == FlowControl.Branch)
                {
                    AddTargetBlocks(
                        block,
                        last.Targets,
                        blockByOffset);
                }
                else if (
                    last.Op.FlowControl ==
                    FlowControl.Cond_Branch)
                {
                    AddTargetBlocks(
                        block,
                        last.Targets,
                        blockByOffset);

                    if (i + 1 < blocks.Count)
                        AddUnique(
                            block.Successors,
                            blocks[i + 1].Id);
                }
                else if (
                    last.Op.FlowControl != FlowControl.Return &&
                    last.Op.FlowControl != FlowControl.Throw)
                {
                    if (i + 1 < blocks.Count)
                        AddUnique(
                            block.Successors,
                            blocks[i + 1].Id);
                }
            }

            return blocks;
        }

        static bool EndsBlock(OpCode op)
        {
            return
                op.FlowControl == FlowControl.Branch ||
                op.FlowControl == FlowControl.Cond_Branch ||
                op.FlowControl == FlowControl.Return ||
                op.FlowControl == FlowControl.Throw;
        }

        static void AddTargetBlocks(
            Block block,
            List<int> targets,
            Dictionary<int, int> blockByOffset)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                int blockId;

                if (!blockByOffset.TryGetValue(
                    targets[i], out blockId))
                {
                    throw new InvalidOperationException(
                        "target block missing IL_" +
                        targets[i].ToString(
                            "X4", CultureInfo.InvariantCulture));
                }

                AddUnique(block.Successors, blockId);
            }
        }

        static void AddUnique(List<int> list, int value)
        {
            if (!list.Contains(value))
                list.Add(value);
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];

                if (body != null &&
                    string.Equals(
                        body.bodyName,
                        name,
                        StringComparison.Ordinal))
                {
                    return body;
                }
            }

            return null;
        }

        static object FindVertexPlanet(object pqs)
        {
            IEnumerable mods =
                ReadMember(pqs, "mods") as IEnumerable;

            if (mods == null) return null;

            foreach (object mod in mods)
            {
                if (mod == null) continue;

                Type type = mod.GetType();
                string name = type.FullName ?? type.Name;

                if (name == "PQSMod_VertexPlanet")
                    return mod;
            }

            return null;
        }

        static object ReadMember(
            object target,
            string name)
        {
            if (target == null) return null;

            Type type = target.GetType();

            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            if (field != null)
                return field.GetValue(target);

            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            if (property != null &&
                property.CanRead &&
                property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(target, null);
            }

            return null;
        }

        static string Token(
            MethodInfo method,
            int token,
            bool isString)
        {
            string prefix =
                "0x" +
                token.ToString(
                    "X8", CultureInfo.InvariantCulture);

            try
            {
                if (isString)
                {
                    return prefix +
                        "=" +
                        Safe(method.Module.ResolveString(token));
                }

                Type[] typeArgs =
                    method.DeclaringType != null &&
                    method.DeclaringType.IsGenericType
                        ? method.DeclaringType.GetGenericArguments()
                        : Type.EmptyTypes;

                Type[] methodArgs =
                    method.IsGenericMethod
                        ? method.GetGenericArguments()
                        : Type.EmptyTypes;

                MemberInfo member =
                    method.Module.ResolveMember(
                        token,
                        typeArgs,
                        methodArgs);

                return prefix +
                    "=" +
                    Safe(MemberName(member));
            }
            catch
            {
                return prefix + "=UNRESOLVED";
            }
        }

        static string MemberName(MemberInfo member)
        {
            if (member == null) return "null";

            Type declaring = member.DeclaringType;

            return
                (declaring == null
                    ? string.Empty
                    : (declaring.FullName ??
                       declaring.Name) + ".") +
                member.Name;
        }

        static string Signature(MethodInfo method)
        {
            ParameterInfo[] parameters =
                method.GetParameters();

            var args =
                new string[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;

                args[i] =
                    type == null
                        ? "NULL"
                        : (type.FullName ?? type.Name);
            }

            Type rt = method.ReturnType;

            return
                method.Name +
                "(" +
                string.Join(",", args) +
                ")->" +
                (rt == null
                    ? "NULL"
                    : (rt.FullName ?? rt.Name));
        }

        static string TargetList(List<int> targets)
        {
            if (targets == null || targets.Count == 0)
                return string.Empty;

            var s = new string[targets.Count];

            for (int i = 0; i < targets.Count; i++)
            {
                s[i] =
                    "IL_" +
                    targets[i].ToString(
                        "X4", CultureInfo.InvariantCulture);
            }

            return string.Join(",", s);
        }

        static string BlockList(List<int> blocks)
        {
            if (blocks == null || blocks.Count == 0)
                return string.Empty;

            var s = new string[blocks.Count];

            for (int i = 0; i < blocks.Count; i++)
            {
                s[i] =
                    "B" +
                    blocks[i].ToString(
                        "D3", CultureInfo.InvariantCulture);
            }

            return string.Join(",", s);
        }

        static Dictionary<ushort, OpCode> BuildCodes()
        {
            var result =
                new Dictionary<ushort, OpCode>();

            FieldInfo[] fields =
                typeof(OpCodes).GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static);

            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(OpCode))
                    continue;

                OpCode op =
                    (OpCode)fields[i].GetValue(null);

                result[unchecked((ushort)op.Value)] = op;
            }

            return result;
        }

        static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);

                var sb =
                    new StringBuilder(
                        digest.Length * 2);

                for (int i = 0; i < digest.Length; i++)
                {
                    sb.Append(
                        digest[i].ToString(
                            "x2",
                            CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        static string Safe(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value
                    .Replace('\n', ' ')
                    .Replace('\r', ' ')
                    .Replace(';', ',')
                    .Replace('|', '/');
        }
    }
}
