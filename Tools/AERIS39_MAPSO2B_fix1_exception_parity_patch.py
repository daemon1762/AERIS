#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit("usage: patch.py <input.cs> <output.cs>")

src_path = pathlib.Path(sys.argv[1])
out_path = pathlib.Path(sys.argv[2])
text = src_path.read_text(encoding="utf-8")

old_decl = "    static double maxAbsError;\n"
new_decl = (
    "    static double maxAbsError;\n"
    "    static long exceptionMatches;\n"
    "    static long exceptionMismatches;\n"
)
if old_decl not in text:
    raise SystemExit("FAIL: maxAbsError anchor not found")
text = text.replace(old_decl, new_decl, 1)

old_summary = (
    '            Console.WriteLine("mismatch_count=" + mismatches.ToString(CultureInfo.InvariantCulture));\n'
    '            Console.WriteLine("max_abs_error=" + maxAbsError.ToString("R", CultureInfo.InvariantCulture));\n'
)
new_summary = (
    '            Console.WriteLine("mismatch_count=" + mismatches.ToString(CultureInfo.InvariantCulture));\n'
    '            Console.WriteLine("exception_match_count=" + exceptionMatches.ToString(CultureInfo.InvariantCulture));\n'
    '            Console.WriteLine("exception_mismatch_count=" + exceptionMismatches.ToString(CultureInfo.InvariantCulture));\n'
    '            Console.WriteLine("max_abs_error=" + maxAbsError.ToString("R", CultureInfo.InvariantCulture));\n'
)
if old_summary not in text:
    raise SystemExit("FAIL: summary anchor not found")
text = text.replace(old_summary, new_summary, 1)

start = text.find("    static void TestCoordinateSurface(object native, Snapshot s, List<PointD> points)\n")
end = text.find("    static List<PointD> BuildPoints(int width, int height, uint seed)\n", start)
if start < 0 or end < 0:
    raise SystemExit("FAIL: TestCoordinateSurface anchors not found")

replacement = r'''    static void TestCoordinateSurface(object native, Snapshot s, List<PointD> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            PointD p = points[i];
            float xf = (float)p.X;
            float yf = (float)p.Y;
            string common = " w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp +
                " x=" + p.X.ToString("R", CultureInfo.InvariantCulture) +
                " y=" + p.Y.ToString("R", CultureInfo.InvariantCulture);

            CheckFloatCall(
                "GetPixelFloat(float,float)" + common,
                delegate { return (float)getPixelFloatSingle.Invoke(native, new object[] { xf, yf }); },
                delegate { return SampleFloatSingle(s, xf, yf); });

            CheckFloatCall(
                "GetPixelFloat(double,double)" + common,
                delegate { return (float)getPixelFloatDouble.Invoke(native, new object[] { p.X, p.Y }); },
                delegate { return SampleFloatDouble(s, p.X, p.Y); });

            CheckColorCall(
                "GetPixelColor(float,float)" + common,
                delegate { return ReadColor(getPixelColorSingle.Invoke(native, new object[] { xf, yf })); },
                delegate { return SampleColorSingle(s, xf, yf); });

            CheckColorCall(
                "GetPixelColor(double,double)" + common,
                delegate { return ReadColor(getPixelColorDouble.Invoke(native, new object[] { p.X, p.Y })); },
                delegate { return SampleColorDouble(s, p.X, p.Y); });

            CheckColorCall(
                "GetPixelColor32(float,float)" + common,
                delegate { return ReadColor(getPixelColor32Single.Invoke(native, new object[] { xf, yf })); },
                delegate { return SampleColor32Single(s, xf, yf); });

            CheckColorCall(
                "GetPixelColor32(double,double)" + common,
                delegate { return ReadColor(getPixelColor32Double.Invoke(native, new object[] { p.X, p.Y })); },
                delegate { return SampleColor32Double(s, p.X, p.Y); });
        }
    }

    static Exception RootException(Exception ex)
    {
        Exception current = ex;
        while (current is TargetInvocationException && current.InnerException != null)
            current = current.InnerException;
        return current;
    }

    static bool SameException(Exception nativeEx, Exception pureEx)
    {
        if (nativeEx == null || pureEx == null) return false;
        return nativeEx.GetType() == pureEx.GetType();
    }

    static void RegisterExceptionOutcome(string label, Exception nativeEx, Exception pureEx)
    {
        nativeEx = nativeEx == null ? null : RootException(nativeEx);
        pureEx = pureEx == null ? null : RootException(pureEx);

        if (SameException(nativeEx, pureEx))
        {
            exceptionMatches++;
            return;
        }

        exceptionMismatches++;
        mismatches++;
        if (printedMismatches < 20)
        {
            printedMismatches++;
            Console.WriteLine("MISMATCH " + label +
                " native_exception=" + (nativeEx == null ? "<none>" : nativeEx.GetType().FullName) +
                " pure_exception=" + (pureEx == null ? "<none>" : pureEx.GetType().FullName));
        }
    }

    static void CheckFloatCall(string label, Func<float> nativeCall, Func<float> pureCall)
    {
        float nativeValue = 0f;
        float pureValue = 0f;
        Exception nativeEx = null;
        Exception pureEx = null;

        try { nativeValue = nativeCall(); }
        catch (Exception ex) { nativeEx = ex; }
        try { pureValue = pureCall(); }
        catch (Exception ex) { pureEx = ex; }

        if (nativeEx == null && pureEx == null)
        {
            CheckFloat(label, nativeValue, pureValue);
            return;
        }

        floatChecks++;
        RegisterExceptionOutcome(label, nativeEx, pureEx);
    }

    static void CheckColorCall(string label, Func<PColor> nativeCall, Func<PColor> pureCall)
    {
        PColor nativeValue = new PColor();
        PColor pureValue = new PColor();
        Exception nativeEx = null;
        Exception pureEx = null;

        try { nativeValue = nativeCall(); }
        catch (Exception ex) { nativeEx = ex; }
        try { pureValue = pureCall(); }
        catch (Exception ex) { pureEx = ex; }

        if (nativeEx == null && pureEx == null)
        {
            CheckColor(label, nativeValue, pureValue);
            return;
        }

        colorChecks++;
        RegisterExceptionOutcome(label, nativeEx, pureEx);
    }

'''

text = text[:start] + replacement + text[end:]
out_path.write_text(text, encoding="utf-8")
print("AERIS39_MAPSO2B_FIX1_PATCH=PASS")
