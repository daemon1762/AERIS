using System;
using System.Collections.Generic;
using UnityEngine;
using AERISFlightControl.API;
using AERISFlightControl.Autopilot;
using AERISFlightControl.Core;
using AERISFlightControl.Logging;
using AERISFlightControl.Protect;
using AERISFlightControl.Settings;
using AERISFlightControl.Terrain;
using AERISFlightControl.Performance;

namespace AERISFlightControl.UI
{
    // Display-only, provider-extensible AERIS Flight Deviation Instrument.
    // FDI and ND are independent user-owned panels; their normalized geometry
    // never grants flight-control authority and survives resolution changes.
    internal sealed class AERISFlightInstrument
    {
        enum FdiTone { White, Green, Amber, Red }
        enum PanelKind { FlightInstrument, NavigationDisplay }
        enum PanelInteraction { None, Move, Resize }

        sealed class DetailLine
        {
            internal string Text;
            internal FdiTone Tone;
            internal DetailLine(string text, FdiTone tone) { Text = text ?? string.Empty; Tone = tone; }
        }

        readonly AERISSettings settings;
        readonly AERISBootstrap core;
        readonly AERISNavigationDisplay navigationDisplay;
        readonly List<AERISFlightInstrumentMessage> extensionMessages = new List<AERISFlightInstrumentMessage>();
        readonly List<DetailLine> detailLines = new List<DetailLine>();
        float nextProviderCollectRealtime;
        GUIStyle labelStyle;
        GUIStyle smallStyle;
        GUIStyle titleStyle;
        GUIStyle panelTitleStyle;
        GUIStyle panelStyle;
        float currentScale = 1f;
        float styleScale = -1f;
        Vector2 detailScrollPosition;
        Rect cachedNavBallRect;
        float nextNavBallRectRefresh;
        float nextNavigationDisplayErrorLogRealtime;
        PanelKind activePanel;
        PanelInteraction activeInteraction;
        Vector2 interactionStartMouse;
        Rect interactionStartRect;
        int interactionControlId;

        const float NavBallDiameter = 190f;
        const float BackgroundAlpha = 0.72f;
        const float BasePanelWidth = 380f;
        const float BasePanelHeight = 244f;
        // AERIS23 rollback recovery: the ND may scale, but its panel geometry may not
        // change aspect ratio. 380:244 keeps the accepted Golden internal map geometry
        // (approximately 366:188 after furniture/margins) invariant at every size.
        const float NavigationPanelAspect = BasePanelWidth / BasePanelHeight;
        const float BaseGap = 12f;
        const float MinimumPanelWidth = 220f;
        const float MinimumPanelHeight = 140f;
        const float ResizeGripSize = 22f;
        const int FdiControlHint = 741230;
        const int NdControlHint = 741231;
        static readonly Color ReadableWhite = new Color(0.96f, 0.98f, 1.00f, 1f);
        static readonly Color ReadableGreen = new Color(0.42f, 1.00f, 0.42f, 1f);
        static readonly Color ReadableAmber = new Color(1.00f, 0.82f, 0.20f, 1f);
        static readonly Color ReadableRed = new Color(1.00f, 0.34f, 0.28f, 1f);
        static readonly Color TextShadow = new Color(0f, 0f, 0f, 0.92f);

        internal AERISFlightInstrument(AERISSettings settings, AERISBootstrap core)
        {
            this.settings = settings;
            this.core = core;
            navigationDisplay = new AERISNavigationDisplay(settings, core);
        }

        internal void Dispose()
        {
            if (navigationDisplay != null) navigationDisplay.Dispose();
        }

        internal void Draw()
        {
            if (settings == null || core == null || !HighLogic.LoadedSceneIsFlight) return;
            Vessel vessel = FlightGlobals.ActiveVessel;
            bool apActive = core.AnyNormalApArmed;
            bool fdiOff = settings.FlightInstrumentDisplayMode == AERISDisplayMode.Off;
            if (!fdiOff)
            {
                CollectExtensionMessages(vessel);
                BuildDetailLines(apActive);
            }
            else
            {
                extensionMessages.Clear();
                detailLines.Clear();
            }

            bool lateralVisible = !fdiOff && apActive && HasLateralChannel();
            bool verticalVisible = !fdiOff && apActive && HasVerticalChannel();
            bool speedVisible = !fdiOff && apActive && HasSpeedTarget();
            bool detailsVisible = !fdiOff && detailLines.Count > 0;
            bool fdiAutoDemand = speedVisible || lateralVisible || verticalVisible ||
                detailsVisible;
            bool fdiVisible = !fdiOff &&
                (settings.FlightInstrumentDisplayMode == AERISDisplayMode.Always ||
                 fdiAutoDemand);
            bool terrainViewportActive = core.Terrain != null &&
                core.Terrain.FlightViewportActive;
            bool ndOff = settings.NavigationDisplayMode == AERISDisplayMode.Off;
            bool ndDisplayViewportActive = terrainViewportActive && !ndOff;
            if (navigationDisplay != null)
                navigationDisplay.SetFlightViewportActive(ndDisplayViewportActive);
            bool ndVisible = ndDisplayViewportActive &&
                (settings.NavigationDisplayMode == AERISDisplayMode.Always ||
                NavigationAutoDemand());
            bool speedOnlyVisible = fdiVisible && speedVisible && !lateralVisible &&
                !verticalVisible && detailLines.Count == 0;
            if (activeInteraction != PanelInteraction.None &&
                ((activePanel == PanelKind.FlightInstrument && !fdiVisible) ||
                 (activePanel == PanelKind.NavigationDisplay && !ndVisible)))
            {
                activeInteraction = PanelInteraction.None;
                if (GUIUtility.hotControl == interactionControlId) GUIUtility.hotControl = 0;
                settings.Save();
            }
            if (!fdiVisible && !ndVisible && !lateralVisible && !verticalVisible) return;

            Rect nav = ResolveNavBallRect();
            float stockUiScale = Mathf.Clamp(nav.width / NavBallDiameter, 0.70f, 1.60f);
            Rect navFurniture = new Rect(nav.xMin - 78f * stockUiScale,
                nav.yMin - 48f * stockUiScale, nav.width + 156f * stockUiScale,
                nav.height + 70f * stockUiScale);
            float resolutionScale = Mathf.Clamp(Mathf.Min(Screen.width / 1920f,
                Screen.height / 1080f), 0.30f, 1.15f);
            float gaugeScale = Mathf.Clamp(resolutionScale, 0.30f, 1.15f);
            float gap = BaseGap * gaugeScale;
            float enlargedGaugeScale = 1.5f * gaugeScale;
            float rightAvailable = Mathf.Max(8f, Screen.width - navFurniture.xMax - 8f);
            float verticalGaugeScale = Mathf.Min(enlargedGaugeScale,
                Mathf.Max(0.25f, rightAvailable / 42f));

            Rect lateralRect = new Rect(nav.center.x - nav.width * enlargedGaugeScale * 0.5f,
                navFurniture.yMin - 24f * enlargedGaugeScale,
                nav.width * enlargedGaugeScale, 20f * enlargedGaugeScale);
            Rect verticalRect = new Rect(navFurniture.xMax + gap,
                nav.y + 22f * gaugeScale, 42f * verticalGaugeScale,
                Mathf.Max(70f * verticalGaugeScale,
                    nav.height * verticalGaugeScale - 44f * verticalGaugeScale));
            lateralRect = ClampToScreen(lateralRect, 4f);
            verticalRect = ClampToScreen(verticalRect, 4f);

            float defaultPanelWidth = BasePanelWidth * resolutionScale;
            float defaultPanelHeight = BasePanelHeight * resolutionScale;
            Rect defaultNdRect = new Rect(navFurniture.xMin - defaultPanelWidth - gap,
                nav.center.y - defaultPanelHeight * 0.5f,
                defaultPanelWidth, defaultPanelHeight);
            Rect defaultFdiRect = new Rect(verticalRect.xMax + gap,
                nav.center.y - defaultPanelHeight * 0.5f,
                defaultPanelWidth, defaultPanelHeight);
            Rect ndRect = ResolvePanelRect(PanelKind.NavigationDisplay, defaultNdRect);
            Rect fdiRect = ResolvePanelRect(PanelKind.FlightInstrument, defaultFdiRect);

            // FDI is drawn above ND when users intentionally overlap them, so it receives
            // the first interaction opportunity as the topmost panel.
            if (fdiVisible) fdiRect = HandlePanelInteraction(PanelKind.FlightInstrument, fdiRect);
            if (ndVisible) ndRect = HandlePanelInteraction(PanelKind.NavigationDisplay, ndRect);

            if (ndVisible)
            {
                try { navigationDisplay.Draw(ndRect); }
                catch (Exception ex)
                {
                    if (Time.realtimeSinceStartup >= nextNavigationDisplayErrorLogRealtime)
                    {
                        nextNavigationDisplayErrorLogRealtime = Time.realtimeSinceStartup + 5f;
                        AERISLogger.Warn("[ND] display exception isolated from flight control: " + ex.Message);
                    }
                }
                DrawResizeGrip(ndRect);
            }
            if (fdiVisible)
            {
                DrawDetails(fdiRect, speedVisible, speedOnlyVisible);
                DrawResizeGrip(fdiRect);
            }

            currentScale = gaugeScale;
            EnsureStyles(gaugeScale);
            if (lateralVisible) DrawLateral(lateralRect);
            if (verticalVisible) DrawVertical(verticalRect);
        }

        bool NavigationAutoDemand()
        {
            if (core == null || core.Terrain == null ||
                !core.Terrain.FlightViewportActive) return false;
            // TERRAIN is the normal airborne ND content. Independent LAND may also
            // demand the display, but neither path grants control authority.
            if (core != null && core.Landing != null && core.Landing.AutoDisplayDemand) return true;
            Vessel vessel = FlightGlobals.ActiveVessel;
            return vessel != null && vessel.mainBody != null && !vessel.LandedOrSplashed &&
                vessel.situation != Vessel.Situations.PRELAUNCH &&
                vessel.situation != Vessel.Situations.SPLASHED;
        }

        void EnsureStyles(float scale)
        {
            if (labelStyle != null && Mathf.Abs(styleScale - scale) < 0.01f) return;
            styleScale = scale;
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(7, Mathf.RoundToInt(12f * scale)),
                clipping = TextClipping.Clip };
            smallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(7, Mathf.RoundToInt(11f * scale)),
                clipping = TextClipping.Clip };
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(7, Mathf.RoundToInt(11f * scale)),
                fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            panelTitleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(7, Mathf.RoundToInt(12f * scale)),
                fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            panelStyle = new GUIStyle(GUI.skin.button);
        }

        Rect ResolveNavBallRect()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextNavBallRectRefresh && cachedNavBallRect.width > 0f) return cachedNavBallRect;
            nextNavBallRectRefresh = now + 0.50f;
            Rect fallback = new Rect(Screen.width * 0.5f - NavBallDiameter * 0.5f,
                Screen.height - NavBallDiameter - 18f, NavBallDiameter, NavBallDiameter);
            try
            {
                Type type = Type.GetType("KSP.UI.Screens.Flight.NavBall, Assembly-CSharp");
                if (type == null) { cachedNavBallRect = fallback; return cachedNavBallRect; }
                UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
                if (objects == null || objects.Length == 0) { cachedNavBallRect = fallback; return cachedNavBallRect; }
                Component component = objects[0] as Component;
                RectTransform transform = component == null ? null : component.GetComponent<RectTransform>();
                if (transform == null) { cachedNavBallRect = fallback; return cachedNavBallRect; }
                Vector3[] corners = new Vector3[4];
                transform.GetWorldCorners(corners);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
                float x = Mathf.Min(a.x, b.x);
                float yBottom = Mathf.Min(a.y, b.y);
                float w = Mathf.Abs(b.x - a.x);
                float h = Mathf.Abs(b.y - a.y);
                Rect candidate = new Rect(x, Screen.height - yBottom - h, w, h);
                bool plausible = w >= 100f && h >= 100f &&
                    w <= Screen.width * 0.35f && h <= Screen.height * 0.35f &&
                    Mathf.Abs(candidate.center.x - Screen.width * 0.5f) <= Screen.width * 0.20f &&
                    candidate.center.y >= Screen.height * 0.62f;
                cachedNavBallRect = plausible ? candidate : fallback;
            }
            catch { cachedNavBallRect = fallback; }
            return cachedNavBallRect;
        }

        Rect ResolvePanelRect(PanelKind kind, Rect defaultRect)
        {
            bool customized = kind == PanelKind.FlightInstrument
                ? settings.FlightInstrumentLayoutCustomized
                : settings.NavigationDisplayLayoutCustomized;
            Rect rect = customized ? ReadNormalizedPanelRect(kind) : defaultRect;
            Rect beforeSizeClamp = rect;
            rect = ClampPanelSize(kind, rect);
            // Migrate any previously saved free-aspect ND size in memory immediately.
            // This never enlarges either old dimension; the next ordinary settings save
            // persists the fixed-ratio geometry. FDI remains independently resizable.
            if (customized && kind == PanelKind.NavigationDisplay &&
                (Mathf.Abs(beforeSizeClamp.width - rect.width) > 0.5f ||
                 Mathf.Abs(beforeSizeClamp.height - rect.height) > 0.5f))
                PersistPanelRect(kind, rect, false);
            if (!customized) return ClampToScreen(rect, 4f);
            if (FullyOutsideScreen(rect))
            {
                rect = ClampToScreen(rect, 4f);
                PersistPanelRect(kind, rect, false);
                settings.Save();
                AERISLogger.Warn("[UI] " + kind +
                    " was completely outside the current game window and was recovered.");
            }
            return rect;
        }

        Rect ReadNormalizedPanelRect(PanelKind kind)
        {
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            if (kind == PanelKind.FlightInstrument)
                return new Rect(settings.FlightInstrumentRectX01 * width,
                    settings.FlightInstrumentRectY01 * height,
                    settings.FlightInstrumentRectW01 * width,
                    settings.FlightInstrumentRectH01 * height);
            return new Rect(settings.NavigationDisplayRectX01 * width,
                settings.NavigationDisplayRectY01 * height,
                settings.NavigationDisplayRectW01 * width,
                settings.NavigationDisplayRectH01 * height);
        }

        void PersistPanelRect(PanelKind kind, Rect rect, bool save)
        {
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            if (kind == PanelKind.FlightInstrument)
            {
                settings.FlightInstrumentLayoutCustomized = true;
                settings.FlightInstrumentRectX01 = rect.x / width;
                settings.FlightInstrumentRectY01 = rect.y / height;
                settings.FlightInstrumentRectW01 = rect.width / width;
                settings.FlightInstrumentRectH01 = rect.height / height;
            }
            else
            {
                settings.NavigationDisplayLayoutCustomized = true;
                settings.NavigationDisplayRectX01 = rect.x / width;
                settings.NavigationDisplayRectY01 = rect.y / height;
                settings.NavigationDisplayRectW01 = rect.width / width;
                settings.NavigationDisplayRectH01 = rect.height / height;
            }
            if (save) settings.Save();
        }

        Rect HandlePanelInteraction(PanelKind kind, Rect rect)
        {
            Event e = Event.current;
            if (e == null) return rect;
            float panelScale = Mathf.Clamp(Mathf.Min(rect.width / BasePanelWidth,
                rect.height / BasePanelHeight), 0.45f, 1.60f);
            float headerHeight = Mathf.Max(18f, 25f * panelScale);
            Rect header = new Rect(rect.x, rect.y,
                Mathf.Max(0f, rect.width - ResizeGripSize), headerHeight);
            Rect grip = ResizeGripRect(rect);
            int controlId = GUIUtility.GetControlID(
                kind == PanelKind.FlightInstrument ? FdiControlHint : NdControlHint,
                FocusType.Passive, rect);

            if (e.type == EventType.MouseDown && e.button == 0 &&
                GUIUtility.hotControl == 0 && (grip.Contains(e.mousePosition) ||
                header.Contains(e.mousePosition)))
            {
                activePanel = kind;
                activeInteraction = grip.Contains(e.mousePosition)
                    ? PanelInteraction.Resize : PanelInteraction.Move;
                interactionStartMouse = e.mousePosition;
                interactionStartRect = rect;
                interactionControlId = controlId;
                PersistPanelRect(kind, rect, false);
                GUIUtility.hotControl = controlId;
                e.Use();
                return rect;
            }
            if (activeInteraction == PanelInteraction.None || activePanel != kind ||
                GUIUtility.hotControl != interactionControlId) return rect;
            if (e.type == EventType.MouseDrag)
            {
                Vector2 delta = e.mousePosition - interactionStartMouse;
                if (activeInteraction == PanelInteraction.Move)
                {
                    rect.x = interactionStartRect.x + delta.x;
                    rect.y = interactionStartRect.y + delta.y;
                }
                else
                {
                    if (kind == PanelKind.NavigationDisplay)
                        rect = ResizeNavigationPanel(interactionStartRect, delta);
                    else
                    {
                        rect.width = interactionStartRect.width + delta.x;
                        rect.height = interactionStartRect.height + delta.y;
                        rect = ClampPanelSize(kind, rect);
                    }
                }
                PersistPanelRect(kind, rect, false);
                GUI.changed = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                activeInteraction = PanelInteraction.None;
                GUIUtility.hotControl = 0;
                rect = ReadNormalizedPanelRect(kind);
                if (FullyOutsideScreen(rect)) rect = ClampToScreen(rect, 4f);
                PersistPanelRect(kind, rect, true);
                if (core != null && core.Performance != null)
                    core.Performance.DisplayLayoutChanged();
                AERISLogger.Info("[UI] " + kind + " layout saved: " +
                    rect.x.ToString("0") + "," + rect.y.ToString("0") + " " +
                    rect.width.ToString("0") + "x" + rect.height.ToString("0"));
                e.Use();
            }
            return rect;
        }

        static Rect ClampPanelSize(PanelKind kind, Rect rect)
        {
            if (kind == PanelKind.NavigationDisplay)
            {
                // Fit the fixed-aspect panel inside the requested bounding box. Using
                // the smaller scale guarantees migration never expands an old free-aspect
                // layout and prevents a resolution change from silently growing ND work.
                float requestedScale = Mathf.Min(
                    rect.width / Mathf.Max(1f, BasePanelWidth),
                    rect.height / Mathf.Max(1f, BasePanelHeight));
                return SetNavigationPanelScale(rect, requestedScale);
            }
            float maxWidth = Mathf.Max(MinimumPanelWidth, Screen.width - 8f);
            float maxHeight = Mathf.Max(MinimumPanelHeight, Screen.height - 8f);
            rect.width = Mathf.Clamp(rect.width, MinimumPanelWidth, maxWidth);
            rect.height = Mathf.Clamp(rect.height, MinimumPanelHeight, maxHeight);
            return rect;
        }

        static Rect ResizeNavigationPanel(Rect start, Vector2 delta)
        {
            float startScale = start.width / Mathf.Max(1f, BasePanelWidth);
            float widthScale = (start.width + delta.x) / Mathf.Max(1f, BasePanelWidth);
            float heightScale = (start.height + delta.y) / Mathf.Max(1f, BasePanelHeight);
            // Whichever axis the user moved farther in normalized scale owns the drag;
            // the other axis follows automatically. Horizontal-only and vertical-only
            // drags therefore both produce a predictable uniform resize.
            float requestedScale = Mathf.Abs(widthScale - startScale) >=
                Mathf.Abs(heightScale - startScale) ? widthScale : heightScale;
            return SetNavigationPanelScale(start, requestedScale);
        }

        static Rect SetNavigationPanelScale(Rect rect, float requestedScale)
        {
            float screenScale = Mathf.Min(
                Mathf.Max(1f, Screen.width - 8f) / BasePanelWidth,
                Mathf.Max(1f, Screen.height - 8f) / BasePanelHeight);
            float minimumScale = Mathf.Max(
                MinimumPanelWidth / BasePanelWidth,
                MinimumPanelHeight / BasePanelHeight);
            float maximumScale = Mathf.Max(0.10f, screenScale);
            minimumScale = Mathf.Min(minimumScale, maximumScale);
            float scale = Mathf.Clamp(requestedScale, minimumScale, maximumScale);
            rect.width = BasePanelWidth * scale;
            rect.height = BasePanelHeight * scale;
            return rect;
        }

        static bool FullyOutsideScreen(Rect rect)
        {
            return rect.xMax <= 0f || rect.yMax <= 0f ||
                rect.xMin >= Screen.width || rect.yMin >= Screen.height;
        }

        static Rect ClampToScreen(Rect rect, float margin)
        {
            rect.x = Mathf.Clamp(rect.x, margin, Mathf.Max(margin, Screen.width - rect.width - margin));
            rect.y = Mathf.Clamp(rect.y, margin, Mathf.Max(margin, Screen.height - rect.height - margin));
            return rect;
        }

        static Rect ResizeGripRect(Rect rect)
        {
            return new Rect(rect.xMax - ResizeGripSize, rect.yMax - ResizeGripSize,
                ResizeGripSize, ResizeGripSize);
        }

        static void DrawResizeGrip(Rect rect)
        {
            Rect grip = ResizeGripRect(rect);
            Color old = GUI.color;
            try
            {
                GUI.color = new Color(0.82f, 0.90f, 0.96f, 0.92f);
                GUI.Box(grip, "↘", GUI.skin.button);
            }
            finally { GUI.color = old; }
        }

        void CollectExtensionMessages(Vessel vessel)
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextProviderCollectRealtime) return;
            nextProviderCollectRealtime = now + 0.20f;
            extensionMessages.Clear();
            AERISFlightInstrumentProviderRegistry.Collect(vessel, extensionMessages);
            extensionMessages.RemoveAll(delegate(AERISFlightInstrumentMessage message)
            {
                if (!message.Active) return true;
                float published = message.PublishedRealtime <= 0f ? now : message.PublishedRealtime;
                return now - published > Mathf.Max(0.30f, Mathf.Clamp(message.HoldSeconds, 0f, 10f));
            });
            extensionMessages.Sort(delegate(AERISFlightInstrumentMessage a, AERISFlightInstrumentMessage b)
            { return ((int)b.Priority).CompareTo((int)a.Priority); });
        }

        bool HasLateralChannel()
        {
            return (core.Hdg != null && core.Hdg.Armed) || (core.Bank != null && core.Bank.Armed);
        }

        bool HasVerticalChannel()
        {
            return (core.Altitude != null && core.Altitude.Armed) ||
                (core.VerticalSpeed != null && core.VerticalSpeed.Armed) || (core.Pitch != null && core.Pitch.Armed);
        }

        bool HasSpeedTarget()
        {
            return (core.Velocity != null && core.Velocity.Armed && core.Velocity.TargetConfirmed) ||
                (core.Acceleration != null && core.Acceleration.Armed);
        }

        int ScaledFont(float value)
        {
            return Mathf.Max(1, Mathf.RoundToInt(value * currentScale));
        }

        void DrawLateral(Rect rect)
        {
            float error, pathScale;
            string mode, value;
            if (core.Hdg != null && core.Hdg.Armed)
            {
                error = core.Hdg.HeadingError; pathScale = 45f; mode = "HDG";
                value = FormatDirectional(error, "°", "RIGHT", "LEFT");
            }
            else
            {
                error = core.Bank != null ? core.Bank.BankError : 0f; pathScale = 30f; mode = "BANK";
                value = FormatDirectional(error, "°", "RIGHT", "LEFT");
            }
            float s = 1.5f * currentScale;
            DrawPanelBackground(rect);
            DrawReadableLabel(new Rect(rect.x + 4f * s, rect.y, 52f * s, rect.height),
                mode, titleStyle, FdiTone.White, ScaledFont(9f));
            Rect bar = new Rect(rect.x + 56f * s, rect.y + 7f * s,
                Mathf.Max(12f * s, rect.width - 126f * s), 6f * s);
            DrawHorizontalDeviationBar(bar, error, pathScale, s);
            DrawReadableLabel(new Rect(rect.xMax - 68f * s, rect.y, 66f * s, rect.height),
                value, smallStyle, FdiTone.White, ScaledFont(8f));
        }

        void DrawVertical(Rect rect)
        {
            float error, pathScale;
            string mode, unit;
            if (core.Altitude != null && core.Altitude.Armed)
            { error = core.Altitude.AltitudeControlErrorMeters; pathScale = 500f; mode = "ALT"; unit = "m"; }
            else if (core.VerticalSpeed != null && core.VerticalSpeed.Armed)
            { error = core.VerticalSpeed.VerticalSpeedErrorMps; pathScale = 20f; mode = "V/S"; unit = "m/s"; }
            else
            { error = core.Pitch != null ? core.Pitch.PitchError : 0f; pathScale = 20f; mode = "PITCH"; unit = "°"; }

            float nominalScale = 1.5f * currentScale;
            float s = Mathf.Min(nominalScale, Mathf.Max(0.20f, rect.width / 42f));
            float verticalTextScale = currentScale * Mathf.Clamp01(
                s / Mathf.Max(0.001f, nominalScale));
            DrawPanelBackground(rect);
            DrawReadableLabel(new Rect(rect.x, rect.y + 1f * s, rect.width, 15f * s),
                mode, titleStyle, FdiTone.White, Mathf.Max(1, Mathf.RoundToInt(8f * verticalTextScale)));
            Rect bar = new Rect(rect.center.x - 2f * s, rect.y + 18f * s,
                4f * s, Mathf.Max(12f * s, rect.height - 58f * s));
            DrawVerticalDeviationBar(bar, error, pathScale, s);
            string magnitude = Mathf.Abs(error).ToString("0.0") + unit;
            DrawReadableLabel(new Rect(rect.x + 1.5f * s, rect.yMax - 39f * s,
                rect.width - 3f * s, 18f * s), magnitude, labelStyle,
                FdiTone.White, Mathf.Max(1, Mathf.RoundToInt(7f * verticalTextScale)));
            bool up = error >= 0f;
            // UP has only two letters and may remain slightly larger. DOWN is
            // deliberately smaller; FitText is still the final overflow guard.
            DrawReadableLabel(new Rect(rect.x + 1.5f * s, rect.yMax - 21f * s,
                rect.width - 3f * s, 18f * s), up ? "UP" : "DOWN",
                titleStyle, FdiTone.White, up ? Mathf.Max(1, Mathf.RoundToInt(8f * verticalTextScale)) : Mathf.Max(1, Mathf.RoundToInt(7f * verticalTextScale)));
        }

        void DrawDetails(Rect rect, bool speedVisible, bool speedOnly)
        {
            float panelScale = Mathf.Clamp(Mathf.Min(rect.width / BasePanelWidth,
                rect.height / BasePanelHeight), 0.45f, 1.60f);
            currentScale = panelScale;
            EnsureStyles(panelScale);
            float margin = Mathf.Max(4f, 7f * panelScale);
            float header = Mathf.Max(18f, 25f * panelScale);
            Color previousColor = GUI.color;
            Color previousBackground = GUI.backgroundColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = new Color(0.05f, 0.08f, 0.11f, 0.96f);
                GUI.Box(rect, GUIContent.none);
                string panelTitle = speedOnly ? "FDI — SPEED GUIDANCE" :
                    "FDI — FLIGHT GUIDANCE";
                DrawReadableLabel(new Rect(rect.x + margin, rect.y,
                    rect.width - margin * 2f, header), panelTitle,
                    panelTitleStyle, FdiTone.White, Mathf.Max(7, Mathf.RoundToInt(8f * panelScale)));

                Rect viewport = new Rect(rect.x + margin, rect.y + header,
                    Mathf.Max(20f, rect.width - margin * 2f),
                    Mathf.Max(20f, rect.height - header - margin));
                GUI.Box(viewport, GUIContent.none);

                float lineHeight = Mathf.Max(12f, 18f * panelScale);
                float speedHeight = speedVisible ? (speedOnly ? 58f : 40f) * panelScale : 0f;
                int visibleLineCount = detailLines.Count;
                if (!speedVisible && visibleLineCount == 0) visibleLineCount = 2;
                float contentHeight = speedHeight + visibleLineCount * lineHeight +
                    12f * panelScale;
                float inset = Mathf.Max(2f, 4f * panelScale);
                Rect scrollViewport = new Rect(viewport.x + inset, viewport.y + inset,
                    Mathf.Max(20f, viewport.width - inset * 2f),
                    Mathf.Max(20f, viewport.height - inset * 2f));
                bool needsScroll = contentHeight > scrollViewport.height;
                float scrollbarAllowance = needsScroll ? Mathf.Max(10f, 18f * panelScale) : 2f;
                Rect content = new Rect(0f, 0f,
                    Mathf.Max(20f, scrollViewport.width - scrollbarAllowance),
                    Mathf.Max(scrollViewport.height, contentHeight));
                if (!needsScroll) detailScrollPosition = Vector2.zero;
                else detailScrollPosition.y = Mathf.Clamp(detailScrollPosition.y, 0f,
                    Mathf.Max(0f, content.height - scrollViewport.height));
                detailScrollPosition = GUI.BeginScrollView(scrollViewport,
                    detailScrollPosition, content, false, needsScroll);
                try
                {
                    float sideInset = Mathf.Max(2f, 3f * panelScale);
                    float y = 0f;
                    if (speedOnly)
                    {
                        string speedMode = core.Velocity != null && core.Velocity.Armed &&
                            core.Velocity.TargetConfirmed ? "VEL" : "ACC";
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            "AP SPEED — " + speedMode + " ONLY", panelTitleStyle,
                            FdiTone.Green, ScaledFont(7f));
                        y += lineHeight;
                    }
                    if (speedVisible && core.Velocity != null && core.Velocity.Armed &&
                        core.Velocity.TargetConfirmed)
                    {
                        float error = core.Velocity.CurrentSurfaceSpeedMps -
                            core.Velocity.TargetSurfaceSpeedMps;
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            "TGT " + core.Velocity.TargetSurfaceSpeedMps.ToString("0.0") + " m/s",
                            smallStyle, FdiTone.Green, ScaledFont(7f));
                        y += lineHeight;
                        string speedError = Mathf.Abs(error).ToString("0.0") + " m/s " +
                            (Mathf.Abs(error) < 0.05f ? "ON TARGET" :
                                (error > 0f ? "FAST" : "SLOW"));
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            speedError, smallStyle, FdiTone.Green, ScaledFont(7f));
                        y += 20f * panelScale;
                    }
                    else if (speedVisible && core.Acceleration != null &&
                        core.Acceleration.Armed)
                    {
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            "ACC TGT " + core.Acceleration.TargetAccelerationMps2.ToString(
                                "+0.0;-0.0;0.0") + " m/s²",
                            smallStyle, FdiTone.Green, ScaledFont(7f));
                        y += lineHeight;
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            "ACC ERR " + core.Acceleration.AccelerationErrorMps2.ToString(
                                "+0.0;-0.0;0.0") + " m/s²",
                            smallStyle, FdiTone.Green, ScaledFont(7f));
                        y += 20f * panelScale;
                    }
                    for (int i = 0; i < detailLines.Count; i++)
                    {
                        DetailLine line = detailLines[i];
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            line.Text, smallStyle, line.Tone, ScaledFont(7f));
                        y += lineHeight;
                    }
                    if (!speedVisible && detailLines.Count == 0)
                    {
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            "STANDBY", smallStyle, FdiTone.Green, ScaledFont(7f));
                        y += lineHeight;
                        DrawReadableLabel(new Rect(sideInset, y,
                            content.width - sideInset * 2f, lineHeight),
                            "NO ACTIVE GUIDANCE OR INTERVENTION", smallStyle,
                            FdiTone.White, ScaledFont(7f));
                    }
                }
                finally { GUI.EndScrollView(); }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackground;
            }
        }

        void BuildDetailLines(bool apActive)
        {
            detailLines.Clear();
            if (core.Terrain != null && core.Terrain.AlertLevel != AERISTerrainAlertLevel.None)
                AddDetail(core.Terrain.AlertText, FdiTone.Red);
            AERISTrafficAlertState trafficAlert;
            if (AERISTrafficAlertApi.TryGetLatest(out trafficAlert))
            {
                string trafficText = "TRAFFIC  " + (trafficAlert.Name ?? string.Empty) +
                    "  CPA " + trafficAlert.ClosestApproachMeters.ToString("0") + "m / " +
                    trafficAlert.ClosestApproachSeconds.ToString("0") + "s";
                AddDetail(trafficText, trafficAlert.ThreatLevel >= 2 ? FdiTone.Red : FdiTone.Amber);
            }
            string extOwner, extTask, extState; bool extAlert;
            if (core.ExternalAutomation != null && core.ExternalAutomation.TryGetDisplay(
                out extOwner, out extTask, out extState, out extAlert))
            {
                AddDetail(extOwner, extAlert ? FdiTone.Red : FdiTone.White);
                AddDetail(extTask, extAlert ? FdiTone.Red : FdiTone.White);
                AddDetail(extState, extAlert ? FdiTone.Red : FdiTone.Green);
                string extPrimary, extSecondary, extResource;
                if (core.ExternalAutomation.TryGetV2SupplementalDisplay(
                    out extPrimary, out extSecondary, out extResource))
                {
                    AddDetail(extPrimary, FdiTone.White);
                    AddDetail(extSecondary, FdiTone.White);
                    AddDetail(extResource, FdiTone.Amber);
                }
            }
            ProtectTelemetry protect = core.Protect;
            if (protect != null)
            {
                if (protect.ProtectActive || protect.StallDetected)
                    AddDetail("ANTI-STALL  " + protect.Risk + " " + protect.StallReason, FdiTone.Red);
                else if (protect.StallWarning)
                    AddDetail("PROTECT  " + protect.Risk + " " + protect.StallReason, FdiTone.Amber);
                if (protect.RequestedAssistThrottle > 0.001f)
                    AddDetail("THRUST ASSIST " + (protect.RequestedAssistThrottle * 100f).ToString("0") + "%",
                        protect.ProtectActive ? FdiTone.Red : FdiTone.Amber);
                if (protect.AutoGearTransitionActive)
                    AddDetail("AUTO GEAR  " + protect.AutoGearStatus, FdiTone.Amber);
            }

            if (core.GroundStability != null)
            {
                if (core.GroundStability.BrakeAssistActive)
                    AddDetail("BRAKE ASSIST " + (core.GroundStability.FinalBrakeDemand * 100f).ToString("0") + "%", FdiTone.Amber);
                if (core.GroundStability.WheelBrakeStockFallbackActive)
                    AddDetail("STOCK BRAKES FALLBACK", FdiTone.Amber);
                if (core.GroundStability.ParkingHoldActive) AddDetail("PARKING HOLD", FdiTone.Amber);
                if (core.GroundStability.ControlActive) AddDetail("GROUND STABILITY", FdiTone.Amber);
            }
            if (core.SpeedAirbrake != null && core.SpeedAirbrake.AppliedDemand > 0.001f)
                AddDetail("AIRBRAKE " + (core.SpeedAirbrake.AppliedDemand * 100f).ToString("0") + "%", FdiTone.Amber);

            for (int i = 0; i < extensionMessages.Count; i++)
            {
                AERISFlightInstrumentMessage message = extensionMessages[i];
                string line = string.IsNullOrEmpty(message.Label) ? message.ProviderId : message.Label;
                if (!string.IsNullOrEmpty(message.Value)) line += " " + message.Value;
                if (!string.IsNullOrEmpty(message.Detail)) line += " — " + message.Detail;
                AddDetail(line, ToneForPriority(message.Priority));
            }
        }

        void AddDetail(string text, FdiTone tone)
        {
            if (!string.IsNullOrEmpty(text)) detailLines.Add(new DetailLine(text, tone));
        }

        static FdiTone ToneForPriority(AERISFlightInstrumentPriority priority)
        {
            if (priority >= AERISFlightInstrumentPriority.Protection) return FdiTone.Red;
            if (priority >= AERISFlightInstrumentPriority.Advisory) return FdiTone.Amber;
            return FdiTone.White;
        }

        void DrawPanelBackground(Rect rect)
        {
            Color old = GUI.color;
            try
            {
                GUI.color = new Color(1f, 1f, 1f, BackgroundAlpha);
                GUI.Box(rect, GUIContent.none, panelStyle);
            }
            finally { GUI.color = old; }
        }

        void DrawReadableLabel(Rect rect, string text, GUIStyle source, FdiTone tone, int minimumFontSize)
        {
            if (rect.width <= 2f || rect.height <= 2f) return;
            GUIStyle style = new GUIStyle(source);
            string fitted = FitText(style, text ?? string.Empty, rect.width - 2f, minimumFontSize);
            Color old = GUI.color;
            try
            {
                GUI.color = TextShadow;
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), fitted, style);
                GUI.color = ToneColor(tone);
                GUI.Label(rect, fitted, style);
            }
            finally { GUI.color = old; }
        }

        static string FitText(GUIStyle style, string text, float width, int minimumFontSize)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int start = Mathf.Max(minimumFontSize, style.fontSize);
            for (int size = start; size >= minimumFontSize; size--)
            {
                style.fontSize = size;
                if (style.CalcSize(new GUIContent(text)).x <= width) return text;
            }
            const string ellipsis = "...";
            string value = text;
            while (value.Length > 1 && style.CalcSize(new GUIContent(value + ellipsis)).x > width)
                value = value.Substring(0, value.Length - 1);
            return value.Length == text.Length ? value : value + ellipsis;
        }

        static Color ToneColor(FdiTone tone)
        {
            return tone == FdiTone.Green ? ReadableGreen :
                (tone == FdiTone.Amber ? ReadableAmber : (tone == FdiTone.Red ? ReadableRed : ReadableWhite));
        }

        static void DrawHorizontalDeviationBar(Rect rect, float error, float pathScale,
            float visualScale)
        {
            DrawLine(new Rect(rect.x, rect.center.y - 0.75f * visualScale, rect.width, 1.5f * visualScale), new Color(0.90f, 0.90f, 0.90f, 0.90f));
            for (int i = -2; i <= 2; i++)
            {
                float x = rect.center.x + i * rect.width / 4f;
                DrawLine(new Rect(x - 0.75f * visualScale, rect.y + 1f * visualScale, 1.5f * visualScale, rect.height - 2f * visualScale), new Color(0.72f, 0.72f, 0.72f, 0.75f));
            }
            float normalized = SignedCompressed(error, pathScale);
            DrawDiamond(new Vector2(rect.center.x + normalized * rect.width * 0.5f, rect.center.y), 3f * visualScale,
                Mathf.Abs(normalized) >= 0.99f ? ReadableRed : new Color(0.20f, 0.84f, 1f, 1f));
        }

        static void DrawVerticalDeviationBar(Rect rect, float error, float pathScale,
            float visualScale)
        {
            DrawLine(new Rect(rect.center.x - 0.75f * visualScale, rect.y, 1.5f * visualScale, rect.height), new Color(0.90f, 0.90f, 0.90f, 0.90f));
            for (int i = -2; i <= 2; i++)
            {
                float y = rect.center.y + i * rect.height / 4f;
                DrawLine(new Rect(rect.x + 1f * visualScale, y - 0.75f * visualScale, rect.width - 2f * visualScale, 1.5f * visualScale), new Color(0.72f, 0.72f, 0.72f, 0.75f));
            }
            float normalized = SignedCompressed(error, pathScale);
            DrawDiamond(new Vector2(rect.center.x, rect.center.y - normalized * rect.height * 0.5f), 3f * visualScale,
                Mathf.Abs(normalized) >= 0.99f ? ReadableRed : new Color(0.20f, 0.84f, 1f, 1f));
        }

        static float SignedCompressed(float value, float scale)
        {
            if (scale <= 0.001f) return 0f;
            float sign = value < 0f ? -1f : 1f;
            return Mathf.Clamp(sign * Mathf.Sqrt(Mathf.Abs(value) / scale), -1f, 1f);
        }

        static void DrawDiamond(Vector2 center, float radius, Color color)
        {
            DrawLine(new Rect(center.x - radius, center.y - 1f, radius * 2f, 2f), color);
            DrawLine(new Rect(center.x - 1f, center.y - radius, 2f, radius * 2f), color);
        }

        static void DrawLine(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f ||
                float.IsNaN(rect.x) || float.IsInfinity(rect.x) ||
                float.IsNaN(rect.y) || float.IsInfinity(rect.y)) return;
            Color old = GUI.color;
            try { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); }
            finally { GUI.color = old; }
        }

        static string FormatDirectional(float value, string unit, string positive, string negative)
        {
            if (Mathf.Abs(value) < 0.05f) return "CENTER";
            return Mathf.Abs(value).ToString(Mathf.Abs(value) >= 100f ? "0" : "0.0") + unit + " " + (value > 0f ? positive : negative);
        }

        static string FormatDistance(float meters)
        { return Mathf.Abs(meters) >= 10000f ? (meters / 1000f).ToString("0.0") + " km" : Mathf.Abs(meters) >= 1000f ? (meters / 1000f).ToString("0.00") + " km" : meters.ToString("0") + " m"; }
        static string FormatSignedDistance(float meters)
        { return meters >= 0f ? FormatDistance(meters) : "PASSED " + FormatDistance(-meters); }
    }
}
