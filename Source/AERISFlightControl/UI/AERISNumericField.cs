using System;
using System.Globalization;
using UnityEngine;

namespace AERISFlightControl.UI
{
    internal static class AERISNumericField
    {
        // Accept ASCII digits, one leading sign, and one decimal point only.
        internal static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            bool signSeen = false;
            bool decimalSeen = false;
            var result = new System.Text.StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c >= '0' && c <= '9') { result.Append(c); continue; }
                if ((c == '+' || c == '-') && !signSeen && result.Length == 0)
                { result.Append(c); signSeen = true; continue; }
                if (c == '.' && !decimalSeen)
                { result.Append(c); decimalSeen = true; continue; }
            }
            return result.ToString();
        }

        internal static string TextField(string value, params GUILayoutOption[] options)
        {
            return Sanitize(GUILayout.TextField(value ?? string.Empty, options));
        }

        internal static bool TryParseSigned(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            return float.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static string Format(float value)
        {
            // Positive numbers are intentionally displayed without '+'. They are interpreted as positive.
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
