using System;
using UnityEngine;

namespace AERISFlightControl.Performance
{
    // GPU is an assistive visual/coarse-candidate backend only.  The safety and runway
    // certification commit paths remain deterministic CPU code even when this is active.
    internal sealed class AERISGpuAssistBackend : IDisposable
    {
        ComputeShader computeShader;
        bool graphicsAssistActive;
        string graphicsAssistName = string.Empty;
        bool disabled;
        string failure = string.Empty;

        internal AERISGpuAssistBackend()
        {
            try
            {
                Supported = SystemInfo.supportsComputeShaders &&
                    SystemInfo.graphicsShaderLevel >= 50;
                Device = SystemInfo.graphicsDeviceType + " / " +
                    SystemInfo.graphicsDeviceName;
                Backend = Supported ? "COMPUTE CAPABLE — CPU FALLBACK ACTIVE" :
                    "COMPUTE UNAVAILABLE — CPU FALLBACK";
            }
            catch (Exception ex)
            {
                Supported = false;
                Device = "UNAVAILABLE";
                Backend = "CAPABILITY PROBE FAILED — CPU FALLBACK";
                failure = ex.GetType().Name;
            }
        }

        internal bool Supported { get; private set; }
        internal bool Active
        {
            get
            {
                return !disabled && ((Supported && computeShader != null) ||
                    graphicsAssistActive);
            }
        }
        internal bool GraphicsAssistActive { get { return graphicsAssistActive && !disabled; } }
        internal string Device { get; private set; }
        internal string Backend { get; private set; }
        internal string Failure { get { return failure; } }
        internal double FrameMilliseconds { get; private set; }

        internal bool RegisterOptionalComputeShader(ComputeShader shader)
        {
            if (!Supported || disabled || shader == null) return false;
            computeShader = shader;
            Backend = graphicsAssistActive ?
                "UNITY COMPUTE + " + graphicsAssistName + " / CPU EXACT VALIDATION" :
                "UNITY COMPUTE ASSIST / CPU EXACT VALIDATION";
            return true;
        }

        // Registers a graphics-only assist (Mesh/Material/RenderTexture). This does not
        // require compute-shader support and does not grant any safety or flight-control
        // authority. It exists so runtime telemetry reports actual ND GPU composition.
        internal bool RegisterGraphicsAssist(string name)
        {
            if (disabled) return false;
            try
            {
                if (SystemInfo.graphicsShaderLevel < 20) return false;
            }
            catch { return false; }
            graphicsAssistActive = true;
            graphicsAssistName = string.IsNullOrEmpty(name) ?
                "UNITY GRAPHICS ASSIST" : name;
            failure = string.Empty;
            Backend = computeShader != null ?
                "UNITY COMPUTE + " + graphicsAssistName + " / CPU EXACT VALIDATION" :
                graphicsAssistName + " / TEMPORAL GPU PRESENTATION / CPU DATA AUTHORITY";
            return true;
        }

        internal void ReleaseGraphicsAssist(string name)
        {
            if (!graphicsAssistActive) return;
            if (!string.IsNullOrEmpty(name) &&
                !string.Equals(name, graphicsAssistName, StringComparison.Ordinal)) return;
            graphicsAssistActive = false;
            graphicsAssistName = string.Empty;
            Backend = computeShader != null ?
                "UNITY COMPUTE ASSIST / CPU EXACT VALIDATION" :
                (Supported ? "COMPUTE CAPABLE — CPU FALLBACK ACTIVE" :
                    "COMPUTE UNAVAILABLE — CPU FALLBACK");
        }

        internal void ReportGraphicsAssistFailure(string name, string reason)
        {
            ReleaseGraphicsAssist(name);
            failure = reason ?? "GRAPHICS ASSIST FAILED";
        }

        internal void RecordFrameCost(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) ||
                milliseconds < 0.0 || milliseconds > 5000.0) return;
            FrameMilliseconds = FrameMilliseconds <= 0.0 ? milliseconds :
                FrameMilliseconds * 0.90 + milliseconds * 0.10;
        }

        internal void DisableAndFallback(string reason)
        {
            disabled = true;
            computeShader = null;
            graphicsAssistActive = false;
            graphicsAssistName = string.Empty;
            failure = reason ?? "GPU BACKEND DISABLED";
            Backend = "GPU DISABLED — CPU FALLBACK";
        }

        public void Dispose()
        {
            computeShader = null;
            graphicsAssistActive = false;
            graphicsAssistName = string.Empty;
            disabled = true;
        }
    }
}
