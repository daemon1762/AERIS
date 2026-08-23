#!/usr/bin/env python3
from pathlib import Path
import hashlib
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER = 'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW'
PARENT = 'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP'

RESOLVER = SRC / 'AERISPtcSourceResolver.cs'
COMPILER = SRC / 'AERISPtcCpuFileExact.cs'
OBSERVER = SRC / 'AERISR031PtcShadowObserver.cs'


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def tree_hash():
    h = hashlib.sha256()
    files = sorted((ROOT / 'Source/AERISFlightControl').rglob('*.cs')) + [CSPROJ]
    for path in files:
        if path == VERSION:
            continue
        h.update(str(path.relative_to(ROOT)).encode())
        h.update(b'\0')
        h.update(path.read_bytes())
        h.update(b'\0')
    return h.hexdigest()

resolver = r'''using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW
    // Shadow-only source discovery. No Terrain DB mutation or producer authority.
    internal enum AERISPtcSourceClassification
    {
        Unknown = 0,
        FileCandidate = 1,
        FileExactCandidate = 2
    }

    internal sealed class AERISPtcRuntimeHeightHint
    {
        internal string BodyName = string.Empty;
        internal string ModType = string.Empty;
        internal string Hint = string.Empty;
        internal bool SimpleHeightMap;
        internal bool HaveDeformity;
        internal double Deformity;
        internal bool HaveOffset;
        internal double Offset;
    }

    internal sealed class AERISPtcBodyCapture
    {
        internal string BodyName = string.Empty;
        internal AERISPtcRuntimeHeightHint[] Hints = new AERISPtcRuntimeHeightHint[0];
        internal double[] WitnessLatitude = new double[0];
        internal double[] WitnessLongitude = new double[0];
        internal double[] WitnessTerrainAsl = new double[0];
    }

    internal sealed class AERISPtcResolvedSource
    {
        internal string BodyName = string.Empty;
        internal AERISPtcSourceClassification Classification;
        internal string SourcePath = string.Empty;
        internal string SourceFormat = string.Empty;
        internal string ModType = string.Empty;
        internal string RuntimeHint = string.Empty;
        internal bool HaveDeformity;
        internal double Deformity;
        internal bool HaveOffset;
        internal double Offset;
        internal string Reason = string.Empty;
    }

    internal sealed class AERISPtcSourceIndex
    {
        internal readonly Dictionary<string, List<string>> ByStem =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, List<string>> ByBaseName =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        internal readonly string GameDataRoot;
        internal int SourceFileCount;
        internal int ConfigFileCount;

        internal AERISPtcSourceIndex(string gameDataRoot)
        {
            GameDataRoot = gameDataRoot ?? string.Empty;
        }
    }

    internal static class AERISPtcSourceResolver
    {
        internal const string Variant =
            "AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW";

        static readonly string[] SourceExtensions =
        {
            ".png", ".dds", ".jpg", ".jpeg", ".tga", ".bmp",
            ".pgm", ".raw", ".bin", ".dat"
        };

        internal static AERISPtcBodyCapture CaptureBody(CelestialBody body)
        {
            var capture = new AERISPtcBodyCapture();
            if (body == null || string.IsNullOrEmpty(body.name)) return capture;
            capture.BodyName = body.name;
            var hints = new List<AERISPtcRuntimeHeightHint>(16);
            try
            {
                object pqs = ReadMember(body, "pqsController");
                object modsObject = ReadMember(pqs, "mods");
                System.Collections.IEnumerable mods = modsObject as System.Collections.IEnumerable;
                if (mods != null)
                {
                    foreach (object mod in mods)
                    {
                        if (mod == null) continue;
                        string type = mod.GetType().FullName ?? mod.GetType().Name;
                        bool heightLike = type.IndexOf("Height", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            type.IndexOf("Map", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!heightLike) continue;
                        bool haveDeformity;
                        bool haveOffset;
                        double deformity = ReadNumeric(mod, "deformity", out haveDeformity);
                        double offset = ReadNumeric(mod, "offset", out haveOffset);
                        bool simple = type.IndexOf("VertexHeightMap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            type.IndexOf("HeightMap", StringComparison.OrdinalIgnoreCase) >= 0;
                        CollectHints(hints, body.name, mod, type, simple,
                            haveDeformity, deformity, haveOffset, offset);
                    }
                }
            }
            catch { }
            capture.Hints = hints.ToArray();
            return capture;
        }

        static void CollectHints(List<AERISPtcRuntimeHeightHint> output, string bodyName,
            object target, string modType, bool simple, bool haveDeformity,
            double deformity, bool haveOffset, double offset)
        {
            if (output == null || target == null) return;
            Type type = target.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < fields.Length && output.Count < 32; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || field.IsStatic) continue;
                string lower = (field.Name ?? string.Empty).ToLowerInvariant();
                if (!(lower.Contains("height") || lower.Contains("map") ||
                    lower.Contains("texture") || lower.Contains("path") ||
                    lower.Contains("file"))) continue;
                object value;
                try { value = field.GetValue(target); } catch { continue; }
                AddHintValues(output, bodyName, modType, field.Name, value, simple,
                    haveDeformity, deformity, haveOffset, offset);
            }
        }

        static void AddHintValues(List<AERISPtcRuntimeHeightHint> output, string bodyName,
            string modType, string member, object value, bool simple,
            bool haveDeformity, double deformity, bool haveOffset, double offset)
        {
            if (value == null || output == null || output.Count >= 32) return;
            var values = new List<string>(4);
            string direct = value as string;
            if (!string.IsNullOrEmpty(direct)) values.Add(direct);
            if (direct == null)
            {
                foreach (string name in new[] { "path", "url", "file", "name" })
                {
                    object nested = null;
                    try { nested = ReadMember(value, name); } catch { }
                    string text = nested as string;
                    if (!string.IsNullOrEmpty(text)) values.Add(text);
                }
            }
            for (int i = 0; i < values.Count && output.Count < 32; i++)
            {
                string hint = NormalizeHint(values[i]);
                if (string.IsNullOrEmpty(hint)) continue;
                bool duplicate = false;
                for (int j = 0; j < output.Count; j++)
                    if (string.Equals(output[j].Hint, hint, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(output[j].ModType, modType, StringComparison.Ordinal))
                    { duplicate = true; break; }
                if (duplicate) continue;
                output.Add(new AERISPtcRuntimeHeightHint
                {
                    BodyName = bodyName ?? string.Empty,
                    ModType = modType ?? string.Empty,
                    Hint = hint,
                    SimpleHeightMap = simple,
                    HaveDeformity = haveDeformity,
                    Deformity = deformity,
                    HaveOffset = haveOffset,
                    Offset = offset
                });
            }
        }

        internal static AERISPtcSourceIndex BuildIndex(string gameDataRoot)
        {
            var index = new AERISPtcSourceIndex(gameDataRoot);
            if (string.IsNullOrEmpty(gameDataRoot) || !Directory.Exists(gameDataRoot))
                return index;
            string[] files;
            try { files = Directory.GetFiles(gameDataRoot, "*", SearchOption.AllDirectories); }
            catch { return index; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string ext = Path.GetExtension(file) ?? string.Empty;
                if (string.Equals(ext, ".cfg", StringComparison.OrdinalIgnoreCase))
                    index.ConfigFileCount++;
                if (!IsSourceExtension(ext)) continue;
                index.SourceFileCount++;
                string relative = Relative(index.GameDataRoot, file);
                string stem = NormalizeHint(Path.ChangeExtension(relative, null));
                string basename = NormalizeHint(Path.GetFileNameWithoutExtension(file));
                AddIndex(index.ByStem, stem, file);
                AddIndex(index.ByBaseName, basename, file);
            }
            return index;
        }

        internal static AERISPtcResolvedSource Resolve(AERISPtcBodyCapture body,
            AERISPtcSourceIndex index)
        {
            var result = new AERISPtcResolvedSource
            {
                BodyName = body == null ? string.Empty : body.BodyName,
                Classification = AERISPtcSourceClassification.Unknown,
                Reason = "NO_UNIQUE_RUNTIME_FILE_SOURCE"
            };
            if (body == null || index == null || body.Hints == null) return result;
            var matches = new Dictionary<string, AERISPtcRuntimeHeightHint>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < body.Hints.Length; i++)
            {
                AERISPtcRuntimeHeightHint hint = body.Hints[i];
                if (hint == null || string.IsNullOrEmpty(hint.Hint)) continue;
                List<string> paths = ResolveHint(index, hint.Hint);
                for (int j = 0; j < paths.Count; j++)
                    if (!matches.ContainsKey(paths[j])) matches[paths[j]] = hint;
            }
            if (matches.Count == 0) return result;
            if (matches.Count > 1)
            {
                result.Classification = AERISPtcSourceClassification.FileCandidate;
                result.Reason = "AMBIGUOUS_RUNTIME_FILE_SOURCE:" + matches.Count;
                return result;
            }
            foreach (KeyValuePair<string, AERISPtcRuntimeHeightHint> pair in matches)
            {
                string ext = (Path.GetExtension(pair.Key) ?? string.Empty).ToLowerInvariant();
                AERISPtcRuntimeHeightHint hint = pair.Value;
                result.SourcePath = pair.Key;
                result.SourceFormat = ext.TrimStart('.').ToUpperInvariant();
                result.ModType = hint.ModType;
                result.RuntimeHint = hint.Hint;
                result.HaveDeformity = hint.HaveDeformity;
                result.Deformity = hint.Deformity;
                result.HaveOffset = hint.HaveOffset;
                result.Offset = hint.Offset;
                bool decoder = ext == ".pgm" || ext == ".raw" || ext == ".bin";
                if (hint.SimpleHeightMap && hint.HaveDeformity && decoder)
                {
                    result.Classification = AERISPtcSourceClassification.FileExactCandidate;
                    result.Reason = "UNIQUE_RUNTIME_SOURCE_SUPPORTED_CPU_DECODER";
                }
                else
                {
                    result.Classification = AERISPtcSourceClassification.FileCandidate;
                    result.Reason = !decoder ? "SOURCE_FOUND_DECODER_NOT_YET_SUPPORTED" :
                        !hint.SimpleHeightMap ? "SOURCE_FOUND_COMPLEX_PQS_SEMANTICS" :
                        "SOURCE_FOUND_MISSING_DEFORMITY";
                }
                break;
            }
            return result;
        }

        static List<string> ResolveHint(AERISPtcSourceIndex index, string rawHint)
        {
            var result = new List<string>();
            string hint = NormalizeHint(rawHint);
            if (hint.StartsWith("gamedata/", StringComparison.OrdinalIgnoreCase))
                hint = hint.Substring(9);
            string ext = Path.GetExtension(hint) ?? string.Empty;
            string stem = NormalizeHint(string.IsNullOrEmpty(ext) ? hint :
                Path.ChangeExtension(hint, null));
            List<string> values;
            if (index.ByStem.TryGetValue(stem, out values)) AddUnique(result, values);
            string basename = NormalizeHint(Path.GetFileNameWithoutExtension(stem));
            if (result.Count == 0 && index.ByBaseName.TryGetValue(basename, out values))
                AddUnique(result, values);
            return result;
        }

        static void AddUnique(List<string> target, List<string> values)
        {
            if (target == null || values == null) return;
            for (int i = 0; i < values.Count; i++)
                if (!target.Contains(values[i])) target.Add(values[i]);
        }

        static void AddIndex(Dictionary<string, List<string>> index, string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;
            List<string> list;
            if (!index.TryGetValue(key, out list))
            {
                list = new List<string>();
                index[key] = list;
            }
            if (!list.Contains(value)) list.Add(value);
        }

        static bool IsSourceExtension(string extension)
        {
            for (int i = 0; i < SourceExtensions.Length; i++)
                if (string.Equals(SourceExtensions[i], extension,
                    StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            return property != null && property.CanRead &&
                property.GetIndexParameters().Length == 0 ? property.GetValue(target, null) : null;
        }

        static double ReadNumeric(object target, string name, out bool found)
        {
            found = false;
            object value = null;
            try { value = ReadMember(target, name); } catch { }
            if (value == null) return 0.0;
            try
            {
                double result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                found = !double.IsNaN(result) && !double.IsInfinity(result);
                return found ? result : 0.0;
            }
            catch { return 0.0; }
        }

        static string Relative(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return path ?? string.Empty;
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedRoot,
                StringComparison.OrdinalIgnoreCase) ? normalizedPath.Substring(normalizedRoot.Length) :
                normalizedPath;
        }

        static string NormalizeHint(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Trim().Trim('"', '\'').Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        }
    }
}
'''

compiler = r'''using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW
    // Pure CPU reference path. It never writes Terrain DB data.
    internal sealed class AERISPtcCpuComparison
    {
        internal string Status = "SKIP";
        internal string Reason = string.Empty;
        internal int Width;
        internal int Height;
        internal int Samples;
        internal double MaximumErrorMeters;
        internal double RmsErrorMeters;
        internal string Transform = string.Empty;
    }

    internal static class AERISPtcCpuFileExact
    {
        sealed class HeightImage
        {
            internal int Width;
            internal int Height;
            internal ushort[] Values = new ushort[0];
            internal int Maximum = 65535;
            internal string Decoder = string.Empty;
        }

        internal static AERISPtcCpuComparison Compare(AERISPtcResolvedSource source,
            AERISPtcBodyCapture body)
        {
            var result = new AERISPtcCpuComparison();
            if (source == null || body == null ||
                source.Classification != AERISPtcSourceClassification.FileExactCandidate)
            {
                result.Reason = "NOT_FILE_EXACT_CANDIDATE";
                return result;
            }
            HeightImage image;
            string decodeReason;
            if (!TryDecode(source.SourcePath, out image, out decodeReason))
            {
                result.Status = "FAIL";
                result.Reason = decodeReason;
                return result;
            }
            result.Width = image.Width;
            result.Height = image.Height;
            int count = Math.Min(body.WitnessTerrainAsl.Length,
                Math.Min(body.WitnessLatitude.Length, body.WitnessLongitude.Length));
            if (count <= 0)
            {
                result.Status = "FAIL";
                result.Reason = "NO_PQS_WITNESS_VALUES";
                return result;
            }

            double bestMax = double.PositiveInfinity;
            double bestRms = double.PositiveInfinity;
            string bestTransform = string.Empty;
            for (int shift = 0; shift < 2; shift++)
                for (int flipU = 0; flipU < 2; flipU++)
                    for (int flipV = 0; flipV < 2; flipV++)
                    {
                        double sumSq = 0.0;
                        double max = 0.0;
                        bool valid = true;
                        for (int i = 0; i < count; i++)
                        {
                            double normalized = Sample(image,
                                body.WitnessLatitude[i], body.WitnessLongitude[i],
                                shift != 0, flipU != 0, flipV != 0);
                            double elevation = (source.HaveOffset ? source.Offset : 0.0) +
                                source.Deformity * normalized;
                            double error = Math.Abs(elevation - body.WitnessTerrainAsl[i]);
                            if (double.IsNaN(error) || double.IsInfinity(error))
                            { valid = false; break; }
                            max = Math.Max(max, error);
                            sumSq += error * error;
                        }
                        if (!valid) continue;
                        double rms = Math.Sqrt(sumSq / count);
                        if (max < bestMax || max == bestMax && rms < bestRms)
                        {
                            bestMax = max;
                            bestRms = rms;
                            bestTransform = "shift180=" + (shift != 0) +
                                ",flipU=" + (flipU != 0) + ",flipV=" + (flipV != 0) +
                                ",decoder=" + image.Decoder;
                        }
                    }
            result.Samples = count;
            result.MaximumErrorMeters = bestMax;
            result.RmsErrorMeters = bestRms;
            result.Transform = bestTransform;
            // Shadow PoC threshold only. Certification remains a later phase and must
            // add broad random witnesses plus the final guard-radius rule.
            if (bestMax <= 2.0 && bestRms <= 0.75)
            {
                result.Status = "PASS";
                result.Reason = "SHADOW_WITNESS_MATCH";
            }
            else
            {
                result.Status = "FAIL";
                result.Reason = "SHADOW_WITNESS_MISMATCH";
            }
            return result;
        }

        static bool TryDecode(string path, out HeightImage image, out string reason)
        {
            image = null;
            reason = string.Empty;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            { reason = "SOURCE_FILE_MISSING"; return false; }
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            try
            {
                if (ext == ".pgm") return TryDecodePgm(path, out image, out reason);
                if (ext == ".raw" || ext == ".bin")
                    return TryDecodeRaw16Square(path, out image, out reason);
                reason = "UNSUPPORTED_CPU_DECODER:" + ext;
                return false;
            }
            catch (Exception ex)
            {
                reason = "DECODE_EXCEPTION:" + ex.GetType().Name;
                return false;
            }
        }

        static bool TryDecodeRaw16Square(string path, out HeightImage image,
            out string reason)
        {
            image = null;
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 || (bytes.Length & 1) != 0)
            { reason = "RAW16_LENGTH_INVALID"; return false; }
            int pixels = bytes.Length / 2;
            int edge = (int)Math.Round(Math.Sqrt(pixels));
            if (edge * edge != pixels)
            { reason = "RAW16_DIMENSIONS_UNKNOWN_NON_SQUARE"; return false; }
            ushort[] little = new ushort[pixels];
            ushort[] big = new ushort[pixels];
            for (int i = 0; i < pixels; i++)
            {
                int p = i * 2;
                little[i] = (ushort)(bytes[p] | bytes[p + 1] << 8);
                big[i] = (ushort)(bytes[p] << 8 | bytes[p + 1]);
            }
            // Choose little-endian here. The shadow resolver does not certify RAW16;
            // a later format adapter will use config metadata to make endianness explicit.
            image = new HeightImage { Width = edge, Height = edge, Values = little,
                Maximum = 65535, Decoder = "RAW16_LE_SQUARE" };
            reason = string.Empty;
            return true;
        }

        static bool TryDecodePgm(string path, out HeightImage image, out string reason)
        {
            image = null;
            byte[] bytes = File.ReadAllBytes(path);
            int cursor = 0;
            string magic = Token(bytes, ref cursor);
            if (magic != "P5" && magic != "P2")
            { reason = "PGM_MAGIC_UNSUPPORTED"; return false; }
            int width, height, maximum;
            if (!int.TryParse(Token(bytes, ref cursor), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out width) ||
                !int.TryParse(Token(bytes, ref cursor), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out height) ||
                !int.TryParse(Token(bytes, ref cursor), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out maximum) ||
                width <= 1 || height <= 1 || maximum <= 0 || maximum > 65535)
            { reason = "PGM_HEADER_INVALID"; return false; }
            int count = checked(width * height);
            ushort[] values = new ushort[count];
            if (magic == "P2")
            {
                for (int i = 0; i < count; i++)
                {
                    int value;
                    if (!int.TryParse(Token(bytes, ref cursor), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out value))
                    { reason = "PGM_ASCII_TRUNCATED"; return false; }
                    values[i] = (ushort)Math.Max(0, Math.Min(maximum, value));
                }
            }
            else
            {
                SkipWhitespace(bytes, ref cursor);
                int bytesPer = maximum < 256 ? 1 : 2;
                if (cursor + count * bytesPer > bytes.Length)
                { reason = "PGM_BINARY_TRUNCATED"; return false; }
                for (int i = 0; i < count; i++)
                {
                    if (bytesPer == 1) values[i] = bytes[cursor++];
                    else values[i] = (ushort)(bytes[cursor++] << 8 | bytes[cursor++]);
                }
            }
            image = new HeightImage { Width = width, Height = height, Values = values,
                Maximum = maximum, Decoder = magic + "_PGM" };
            reason = string.Empty;
            return true;
        }

        static string Token(byte[] bytes, ref int cursor)
        {
            SkipWhitespace(bytes, ref cursor);
            if (cursor >= bytes.Length) return string.Empty;
            int start = cursor;
            while (cursor < bytes.Length && !char.IsWhiteSpace((char)bytes[cursor]) &&
                bytes[cursor] != '#') cursor++;
            return System.Text.Encoding.ASCII.GetString(bytes, start, cursor - start);
        }

        static void SkipWhitespace(byte[] bytes, ref int cursor)
        {
            while (cursor < bytes.Length)
            {
                if (char.IsWhiteSpace((char)bytes[cursor])) { cursor++; continue; }
                if (bytes[cursor] == '#')
                {
                    while (cursor < bytes.Length && bytes[cursor] != '\n' &&
                        bytes[cursor] != '\r') cursor++;
                    continue;
                }
                break;
            }
        }

        static double Sample(HeightImage image, double latitudeDeg, double longitudeDeg,
            bool shift180, bool flipU, bool flipV)
        {
            double lon = longitudeDeg;
            if (shift180) lon += 180.0;
            while (lon < -180.0) lon += 360.0;
            while (lon >= 180.0) lon -= 360.0;
            double u = (lon + 180.0) / 360.0;
            double v = (90.0 - Math.Max(-90.0, Math.Min(90.0, latitudeDeg))) / 180.0;
            if (flipU) u = 1.0 - u;
            if (flipV) v = 1.0 - v;
            u = u - Math.Floor(u);
            v = Math.Max(0.0, Math.Min(1.0, v));
            double x = u * (image.Width - 1);
            double y = v * (image.Height - 1);
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = Math.Min(image.Width - 1, x0 + 1);
            int y1 = Math.Min(image.Height - 1, y0 + 1);
            double tx = x - x0;
            double ty = y - y0;
            double a = Value(image, x0, y0) * (1.0 - tx) + Value(image, x1, y0) * tx;
            double b = Value(image, x0, y1) * (1.0 - tx) + Value(image, x1, y1) * tx;
            return a * (1.0 - ty) + b * ty;
        }

        static double Value(HeightImage image, int x, int y)
        {
            return image.Values[y * image.Width + x] /
                (double)Math.Max(1, image.Maximum);
        }
    }
}
'''

observer = r'''using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR031PtcShadowObserver : MonoBehaviour
    {
        sealed class ShadowResult
        {
            internal AERISPtcSourceIndex Index;
            internal AERISPtcResolvedSource[] Sources = new AERISPtcResolvedSource[0];
            internal AERISPtcCpuComparison[] Comparisons = new AERISPtcCpuComparison[0];
            internal double WorkerMilliseconds;
        }

        static readonly double[,] WitnessPoints = new double[,]
        {
            { -67.5, -157.5 }, { -52.5, -82.5 }, { -37.5, -7.5 },
            { -22.5, 67.5 }, { -7.5, 142.5 }, { 7.5, -142.5 },
            { 22.5, -67.5 }, { 37.5, 7.5 }, { 52.5, 82.5 },
            { 67.5, 157.5 }, { 11.25, 33.75 }, { -11.25, -146.25 }
        };

        float nextAttempt;
        bool submitted;

        void Update()
        {
            if (submitted || Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (!AERISTerrainTileSystem.GameDataHashReady ||
                FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return;

            AERISPtcBodyCapture[] captures = CaptureBodies();
            string gameData = Path.Combine(KSPUtil.ApplicationRootPath ?? string.Empty,
                "GameData");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.GeneralCompute, "r031-ptc-source-resolver-shadow",
                runtime.CaptureStamp(), context =>
                {
                    var result = new ShadowResult();
                    var workerWatch = System.Diagnostics.Stopwatch.StartNew();
                    result.Index = AERISPtcSourceResolver.BuildIndex(gameData);
                    result.Sources = new AERISPtcResolvedSource[captures.Length];
                    result.Comparisons = new AERISPtcCpuComparison[captures.Length];
                    for (int i = 0; i < captures.Length; i++)
                    {
                        result.Sources[i] = AERISPtcSourceResolver.Resolve(captures[i],
                            result.Index);
                        result.Comparisons[i] = AERISPtcCpuFileExact.Compare(
                            result.Sources[i], captures[i]);
                    }
                    workerWatch.Stop();
                    result.WorkerMilliseconds = workerWatch.Elapsed.TotalMilliseconds;
                    return result;
                }, value =>
                {
                    ShadowResult result = value as ShadowResult;
                    if (result == null)
                    {
                        AERISLogger.Warn("[R031][PTC] event=SHADOW_FAIL; reason=NULL_RESULT");
                        return;
                    }
                    LogResult(result, captures, watch.Elapsed.TotalMilliseconds);
                }, false);
            if (accepted)
            {
                submitted = true;
                AERISLogger.Info("[R031][PTC] event=SHADOW_SUBMIT; bodies=" +
                    captures.Length + "; authority=NONE; db_write=false; gpu=false");
            }
        }

        static AERISPtcBodyCapture[] CaptureBodies()
        {
            var result = new List<AERISPtcBodyCapture>();
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null || string.IsNullOrEmpty(body.name) ||
                    !AERISTerrainTileSystem.BodyHasSolidSurface(body)) continue;
                AERISPtcBodyCapture capture = AERISPtcSourceResolver.CaptureBody(body);
                int count = WitnessPoints.GetLength(0);
                capture.WitnessLatitude = new double[count];
                capture.WitnessLongitude = new double[count];
                capture.WitnessTerrainAsl = new double[count];
                bool valid = true;
                for (int p = 0; p < count; p++)
                {
                    double latitude = WitnessPoints[p, 0];
                    double longitude = WitnessPoints[p, 1];
                    double elevation;
                    capture.WitnessLatitude[p] = latitude;
                    capture.WitnessLongitude[p] = longitude;
                    if (!AERISTerrainAwareness.TrySampleTerrainAslShared(body,
                        latitude, longitude, out elevation))
                    { valid = false; break; }
                    capture.WitnessTerrainAsl[p] = elevation;
                }
                if (!valid)
                {
                    capture.WitnessLatitude = new double[0];
                    capture.WitnessLongitude = new double[0];
                    capture.WitnessTerrainAsl = new double[0];
                }
                result.Add(capture);
            }
            return result.ToArray();
        }

        static void LogResult(ShadowResult result, AERISPtcBodyCapture[] captures,
            double wallMilliseconds)
        {
            int unknown = 0;
            int candidates = 0;
            int exactCandidates = 0;
            int cpuPass = 0;
            int cpuFail = 0;
            for (int i = 0; i < result.Sources.Length; i++)
            {
                AERISPtcResolvedSource source = result.Sources[i];
                AERISPtcCpuComparison cpu = result.Comparisons[i];
                AERISPtcBodyCapture capture = i < captures.Length ? captures[i] : null;
                if (source.Classification == AERISPtcSourceClassification.Unknown) unknown++;
                else if (source.Classification == AERISPtcSourceClassification.FileExactCandidate)
                    exactCandidates++;
                else candidates++;
                if (cpu != null && cpu.Status == "PASS") cpuPass++;
                else if (cpu != null && cpu.Status == "FAIL") cpuFail++;
                AERISLogger.Info("[R031][PTC_RESOLVE] body=" + Safe(source.BodyName) +
                    "; classification=" + source.Classification +
                    "; source=" + Safe(source.SourcePath) +
                    "; format=" + Safe(source.SourceFormat) +
                    "; mod=" + Safe(source.ModType) +
                    "; hint=" + Safe(source.RuntimeHint) +
                    "; deformity=" + (source.HaveDeformity ?
                        source.Deformity.ToString("R", CultureInfo.InvariantCulture) : "NA") +
                    "; offset=" + (source.HaveOffset ?
                        source.Offset.ToString("R", CultureInfo.InvariantCulture) : "0") +
                    "; runtime_hints=" + (capture == null || capture.Hints == null ? 0 :
                        capture.Hints.Length) + "; reason=" + Safe(source.Reason));
                if (cpu != null)
                    AERISLogger.Info("[R031][PTC_CPU] body=" + Safe(source.BodyName) +
                        "; status=" + cpu.Status + "; reason=" + Safe(cpu.Reason) +
                        "; size=" + cpu.Width + "x" + cpu.Height +
                        "; samples=" + cpu.Samples +
                        "; max_error_m=" + cpu.MaximumErrorMeters.ToString("0.000",
                            CultureInfo.InvariantCulture) +
                        "; rms_error_m=" + cpu.RmsErrorMeters.ToString("0.000",
                            CultureInfo.InvariantCulture) +
                        "; transform=" + Safe(cpu.Transform) +
                        "; certification=NO_SHADOW_ONLY");
            }
            AERISLogger.Info("[R031][PTC] event=SHADOW_COMPLETE" +
                "; solid_bodies=" + result.Sources.Length +
                "; source_files=" + (result.Index == null ? 0 : result.Index.SourceFileCount) +
                "; config_files=" + (result.Index == null ? 0 : result.Index.ConfigFileCount) +
                "; unknown=" + unknown + "; file_candidate=" + candidates +
                "; file_exact_candidate=" + exactCandidates +
                "; cpu_pass=" + cpuPass + "; cpu_fail=" + cpuFail +
                "; worker_ms=" + result.WorkerMilliseconds.ToString("0.0",
                    CultureInfo.InvariantCulture) +
                "; wall_ms=" + wallMilliseconds.ToString("0.0",
                    CultureInfo.InvariantCulture) +
                "; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(';', ',')
                .Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/');
        }
    }
}
'''

for path, text in ((RESOLVER, resolver), (COMPILER, compiler), (OBSERVER, observer)):
    if path.exists() and path.read_text() != text:
        raise SystemExit('R031 source exists with unexpected content: ' + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)

csproj = CSPROJ.read_text()
anchor = '    <Compile Include="Terrain\\AERISTerrainPreloadContracts.cs" />\n'
includes = (
    '    <Compile Include="Terrain\\AERISPtcSourceResolver.cs" />\n'
    '    <Compile Include="Terrain\\AERISPtcCpuFileExact.cs" />\n'
    '    <Compile Include="Terrain\\AERISR031PtcShadowObserver.cs" />\n'
)
if 'AERISPtcSourceResolver.cs' not in csproj:
    if anchor not in csproj:
        raise SystemExit('R031 csproj anchor missing')
    csproj = csproj.replace(anchor, anchor + includes, 1)
    CSPROJ.write_text(csproj)

version = VERSION.read_text()
if PARENT not in version and 'R030 FIX2' not in version:
    raise SystemExit('R031 requires materialized R030 Fix2 parent identity')
version = re.sub(r'internal const string Display = ".*?";',
    'internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC SOURCE RESOLVER + CPU FILE_EXACT SHADOW";', version)
version = re.sub(r'internal const string UiCheckpoint = ".*?";',
    'internal const string UiCheckpoint = "DEV CP3.75 — AERIS32 — REV3.5 R031 — PTC SOURCE RESOLVER + CPU FILE_EXACT SHADOW";', version)
version = re.sub(r'internal const string CandidateName = ".*?";',
    'internal const string CandidateName = "' + MARKER + '";', version)
head = __import__('subprocess').check_output(['git','rev-parse','HEAD'], cwd=str(ROOT), text=True).strip()
version = re.sub(r'internal const string SourceGitSha = ".*?";',
    'internal const string SourceGitSha = "' + head + '";', version)
VERSION.write_text(version)
source_hash = tree_hash()
version = VERSION.read_text()
version = re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
    'internal const string SourceTreeSha256 = "' + source_hash + '";', version)
VERSION.write_text(version)

print('PASS: materialized R031 PTC source resolver + CPU FILE_EXACT shadow')
print('runtime_change=SHADOW_OBSERVER_ONLY')
print('db_write=NO producer_switch=NO gpu=NO')
print('cpu_decoders=PGM_P2_P5,RAW16_LE_SQUARE')
print('certification=NO')
print('resolver_sha256=' + sha256(RESOLVER))
print('compiler_sha256=' + sha256(COMPILER))
print('observer_sha256=' + sha256(OBSERVER))
print('source_tree_sha256=' + source_hash)
