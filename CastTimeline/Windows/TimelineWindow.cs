using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CastTimeline.Utilities;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace CastTimeline.Windows;

/// <summary>Values resolved once per frame in DrawTimelineVisualization and forwarded into DrawCastEvent.</summary>
internal readonly struct DrawParams
{
    public readonly float   Scale;
    public readonly float   IconSize;
    public readonly float   OGcdSize;
    public readonly bool    UseCustomTrailColor;
    // Pre-packed custom trail colour (used only when UseCustomTrailColor is true).
    public readonly uint    CustomTrailColor;
    public readonly bool    ShowIconLabels;
    public readonly bool    WindowHovered;
    public readonly Vector2 MousePos;

    public DrawParams(float scale, float iconSize, float oGcdSize, bool useCustomTrailColor, uint customTrailColor, bool showIconLabels, bool windowHovered, Vector2 mousePos)
    {
        Scale               = scale;
        IconSize            = iconSize;
        OGcdSize            = oGcdSize;
        UseCustomTrailColor = useCustomTrailColor;
        CustomTrailColor    = customTrailColor;
        ShowIconLabels      = showIconLabels;
        WindowHovered       = windowHovered;
        MousePos            = mousePos;
    }
}

public class TimelineWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private uint selectedJobId = 0;
    private bool showAllJobs = true;
    private bool isDragging = false;
    private Vector2 windowStartPos = Vector2.Zero;
    private bool isResizing = false;
    private Vector2 resizeStartSize = Vector2.Zero;
    private int resizeEdge = 0; // 1=right, 2=bottom, 3=right-bottom

    private PlayerCastData? currentPlayerCastData;
    public PlayerCastData? CurrentPlayerCastData => currentPlayerCastData;

    // Filtered list cache — rebuilt only when the job filter or cast data changes,
    // not every draw frame. When showAllJobs is true the source list is used directly.
    private List<CastLogEntry>? cachedFilteredLogs;
    private uint cachedFilterJobId = uint.MaxValue;

    // Replay state — driven by Plugin.OnFrameworkUpdate each game tick
    private bool isReplaying;
    private float currentFightTimeMs; // milliseconds from pull; negative during countdown

    private bool resetScroll;

    // Cached per-dataset values — recomputed whenever the active list changes.
    private uint  cachedMaxTimestamp;
    private float cachedEarliestEffectiveMs;

    // Cached custom trail color — repacked only when settings.TrailColor changes.
    private Vector3 cachedTrailColorInput;
    private uint    cachedCustomTrailColorU32;

    // Cached ruler labels — rebuilt only when fight duration or ruler interval changes.
    private Dictionary<long, string> cachedRulerLabels         = new();
    private uint cachedRulerLabelMaxTimestamp                   = uint.MaxValue;
    private int  cachedRulerLabelIntervalSeconds                = -1;

    // Lead-in prepended to content during replay so that t=0 icons approach from the right
    // during the countdown window instead of being flush with the left edge.
    // 10 seconds covers the maximum /countdown value in FFXIV.
    private const float LeadInMs = 10_000f;

    // Static lead-in is user-configurable via TimelineWindowSettings.StaticLeadInSeconds.

    // The playhead (vertical white line) sits at this fraction of the visible window width.
    private const float PlayheadFraction = 0.25f;

    private static readonly long[] PreStartMarkersMs = [-1000, -2000, -3000, -4000, -5000, -6000, -7000, -10000, -12000, -15000];

    // Built once on the first potion/item lookup; null until then.
    private static Dictionary<string, uint>? ItemIconByName;

    // Populated at SetPlayerCastData time so DrawCastEvent never calls GetFromGameIcon per frame.
    private static readonly Dictionary<uint, ISharedImmediateTexture> IconTextureCache = new();

    // Pre-packed colours used every draw frame — computed once at class load.
    private static readonly uint LabelBgColor   = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.65f));
    private static readonly uint LabelTextColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
    private static readonly uint PlayheadColor  = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f));
    private static readonly uint RulerBgColor   = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.3f));

    // Caches CalcTextSize results for ability-type label strings (tiny set: "0", "1", etc.).
    private static readonly Dictionary<string, Vector2> AbilityTypeSizeCache = new();

    public bool IsReplaying => isReplaying;

    public void StartReplay()
    {
        isReplaying = true;
        currentFightTimeMs = 0f;
    }

    public void StopReplay()
    {
        isReplaying = false;
        currentFightTimeMs = 0f;
        resetScroll = true;
    }

    public void UpdateReplayTime(float fightTimeMs)
    {
        currentFightTimeMs = fightTimeMs;
    }

    public TimelineWindow(Plugin plugin)
        : base("Timeline", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;

        // Set initial position and size from configuration
        var settings = plugin.Configuration.TimelineWindow;
        Position = settings.WindowPosition;
        Size = settings.WindowSize;

        UpdateWindowFlags();
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        UpdateWindowFlags();

        // Apply window position if remember position is enabled
        var settings = plugin.Configuration.TimelineWindow;
        if (settings.RememberPosition)
        {
            Position = settings.WindowPosition;
            Size = settings.WindowSize;
        }
    }

    public override void Draw()
    {
        var settings = plugin.Configuration.TimelineWindow;

        // Draw background with custom color and alpha
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        // Draw background
        var bgColor = new Vector4(
            settings.BackgroundColor.X,
            settings.BackgroundColor.Y,
            settings.BackgroundColor.Z,
            settings.BackgroundAlpha);

        drawList.AddRectFilled(windowPos, windowPos + windowSize, ImGui.GetColorU32(bgColor));

        // Draw outline when unlocked
        if (!settings.IsLocked && settings.ShowOutlineWhenUnlocked)
        {
            drawList.AddRect(
                windowPos,
                windowPos + windowSize,
                ImGui.GetColorU32(settings.OutlineColor),
                0f,
                ImDrawFlags.None,
                settings.OutlineThickness);
        }

        // Handle dragging when unlocked
        HandleWindowDragging();

        // Handle resizing when unlocked
        HandleWindowResizing();

        // Draw timeline content
        DrawTimelineContent();

        // Draw lock/unlock button
        DrawLockButton();

        // Right-click context menu for timeline selection
        DrawContextMenu();
    }

    private void UpdateWindowFlags()
    {
        var settings = plugin.Configuration.TimelineWindow;

        // Update flags based on lock state
        if (settings.IsLocked)
        {
            Flags = ImGuiWindowFlags.NoTitleBar |
                   ImGuiWindowFlags.NoScrollbar |
                   ImGuiWindowFlags.NoScrollWithMouse |
                   ImGuiWindowFlags.NoMove |
                   ImGuiWindowFlags.AlwaysAutoResize |
                   ImGuiWindowFlags.NoBackground;
        }
        else
        {
            Flags = ImGuiWindowFlags.NoTitleBar |
                   ImGuiWindowFlags.NoScrollbar |
                   ImGuiWindowFlags.NoScrollWithMouse |
                   ImGuiWindowFlags.NoBackground;
        }
    }

    private void HandleWindowDragging()
    {
        var settings = plugin.Configuration.TimelineWindow;

        if (settings.IsLocked) return;

        var windowPos = ImGui.GetWindowPos();
        var mousePos = ImGui.GetMousePos();
        var windowSize = ImGui.GetWindowSize();

        // Check if mouse is anywhere within the window
        if (ImGui.IsMouseHoveringRect(windowPos, windowPos + windowSize) && resizeEdge == 0)
        {
            // Change cursor to indicate draggable
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                isDragging = true;
                // dragStartPos = mousePos; // unused; delta from ImGui.GetMouseDragDelta
                windowStartPos = windowPos;
            }
        }

        if (isDragging && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            var newPos = windowStartPos + delta;

            settings.WindowPosition = newPos;
            Position = newPos;
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            isDragging = false;
            isResizing = false;
            plugin.Configuration.Save();
        }
    }

    private void HandleWindowResizing()
    {
        var settings = plugin.Configuration.TimelineWindow;

        if (settings.IsLocked) return;

        var windowPos = ImGui.GetWindowPos();
        var mousePos = ImGui.GetMousePos();
        var windowSize = ImGui.GetWindowSize();
        const float resizeHandleSize = 12f;

        // Check if mouse is near window edges for resizing
        resizeEdge = 0;

        // Bottom right corner only - strict corner detection
        if (mousePos.X >= windowPos.X + windowSize.X - resizeHandleSize &&
            mousePos.X <= windowPos.X + windowSize.X &&
            mousePos.Y >= windowPos.Y + windowSize.Y - resizeHandleSize &&
            mousePos.Y <= windowPos.Y + windowSize.Y)
        {
            resizeEdge = 3;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        // Start resizing
        if (resizeEdge > 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            isResizing = true;
            // resizeStartPos = mousePos; // unused; delta from ImGui.GetMouseDragDelta
            resizeStartSize = windowSize;
        }

        // Handle resizing
        if (isResizing && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            var newSize = resizeStartSize;

            // Bottom right corner only
            if (resizeEdge == 3)
            {
                newSize.X = Math.Max(200f, resizeStartSize.X + delta.X);
                newSize.Y = Math.Max(100f, resizeStartSize.Y + delta.Y);

                settings.WindowSize = newSize;
                Size = newSize;
            }
        }
    }

    private void DrawTimelineContent()
    {
        if (currentPlayerCastData != null)
            DrawPlayerCastContent();
        else
            ImGui.Text("No timeline selected. Open the main window to select a timeline.");
    }

    private void DrawPlayerCastContent()
    {
        if (currentPlayerCastData == null) return;

        List<CastLogEntry> logs;
        if (showAllJobs)
        {
            logs = currentPlayerCastData.CastLogs;
        }
        else
        {
            if (cachedFilterJobId != selectedJobId)
            {
                cachedFilteredLogs = currentPlayerCastData.CastLogs
                    .Where(log => log.SourceJobId == selectedJobId)
                    .ToList();
                cachedFilterJobId = selectedJobId;
                RebuildTimestampCache(cachedFilteredLogs);
            }
            logs = cachedFilteredLogs!;
        }

        if (logs.Count == 0)
        {
            ImGui.Text("No cast logs to display.");
            return;
        }

        DrawTimelineVisualization(logs);
    }

    private const float RulerHeight = 20f;

    // oGCD icons render at this fraction of the full GCD icon size in the upper lane
    private const float OGcdSizeRatio = 0.55f;

    // Coordinate system:
    //   Content pixel X for a cast at timestamp T (ms) = (T + leadInMs) * scale / 10f
    //   Screen pixel X = childPos.X + contentX - scrollX
    //
    // The playhead sits at screenX = childPos.X + availWidth * PlayheadFraction.
    // Auto-scroll drives scrollX so that the icon for currentFightTimeMs lands on the playhead:
    //   scrollX = (currentFightTimeMs + leadInMs) * scale / 10f - playheadOffset
    private void DrawTimelineVisualization(List<CastLogEntry> filteredLogs)
    {
        var settings = plugin.Configuration.TimelineWindow;
        var scale    = settings.TimelineScale;
        var iconSize = 24f * settings.IconScale;
        var oGcdSize = iconSize * OGcdSizeRatio;

        var trailVec = settings.TrailColor;
        if (trailVec != cachedTrailColorInput)
        {
            cachedTrailColorInput     = trailVec;
            cachedCustomTrailColorU32 = ImGui.GetColorU32(new Vector4(trailVec.X, trailVec.Y, trailVec.Z, 0.6f));
        }

        // During replay use the full 10 s countdown lead-in.
        // Otherwise derive the lead-in from the cached earliest effective start so that it
        // always sits exactly 1.5 s from the left edge, regardless of precast depth.
        var leadInMs = isReplaying ? LeadInMs : 1500f - cachedEarliestEffectiveMs;
        var totalWidth = (leadInMs + cachedMaxTimestamp) * scale / 10f + 200f;

        // Two-lane row: oGCDs occupy the upper lane, GCDs the lower lane.
        const float lanePadding = 4f;
        var rowHeight = oGcdSize + lanePadding + iconSize;

        const float scrollbarAllowance = 17f;
        var rulerHeight = settings.ShowRuler ? RulerHeight : 0f;
        var contentHeight = rulerHeight + rowHeight;
        // Reserve scrollbar space only when the scrollbar is visible.
        var childHeight = contentHeight + (isReplaying ? 0f : scrollbarAllowance);

        // Capture layout info before entering the child window
        var childPos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var playheadOffset = availWidth * PlayheadFraction;

        var childFlags = isReplaying
            ? ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            : ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.SetNextWindowContentSize(new Vector2(totalWidth, 0));
        ImGui.BeginChild("TimelineView", new Vector2(availWidth, childHeight), false, childFlags);

        if (resetScroll) { ImGui.SetScrollX(0); resetScroll = false; }

        if (isReplaying)
        {
            // Drive scroll so that the icon at currentFightTimeMs sits on the playhead
            var nowContentX = (currentFightTimeMs + leadInMs) * scale / 10f;
            ImGui.SetScrollX(Math.Max(0f, nowContentX - playheadOffset));
        }
        else if (ImGui.IsWindowHovered())
        {
            // Manual horizontal scroll via mouse wheel when not replaying
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0)
                ImGui.SetScrollX(Math.Max(0f, ImGui.GetScrollX() - wheel * iconSize * 2f));
        }

        if (settings.ShowRuler)
            DrawTimelineRuler(cachedMaxTimestamp, totalWidth, leadInMs, childPos.X, childPos.X + availWidth);

        var dp = new DrawParams(
            scale,
            iconSize,
            oGcdSize,
            settings.UseCustomTrailColor,
            cachedCustomTrailColorU32,
            settings.ShowIconLabels,
            ImGui.IsWindowHovered(),
            ImGui.GetMousePos());

        var drawList = ImGui.GetWindowDrawList();
        var rowTop = ImGui.GetCursorScreenPos();

        var scrollX = ImGui.GetScrollX();
        // Buffer accounts for the largest icon size on either side plus a worst-case
        // cast trail. Icons are at most iconSize wide; trails extend rightward by
        // CastTime * scale * 100f which can reach several hundred pixels for long casts.
        const float cullBuffer = 500f;
        var cullLeft  = scrollX - cullBuffer;
        var cullRight = scrollX + availWidth + cullBuffer;

        foreach (var log in filteredLogs)
        {
            // Content-space X of the icon's left edge (same formula as DrawCastEvent).
            var contentX = (log.Timestamp + leadInMs) * dp.Scale / 10f;
            if (log.IsPrecast)
                contentX -= (float)(log.CastTime * dp.Scale * 100f);

            // Right edge = icon left + full cast width (icon + trail).
            var castWidthPx = log.IsInstant || log.CastTime <= 0
                ? dp.IconSize
                : (float)(log.CastTime * dp.Scale * 100f);
            var rightEdge = contentX + castWidthPx;

            if (rightEdge < cullLeft || contentX > cullRight)
                continue;

            DrawCastEvent(log, drawList, rowTop, lanePadding, leadInMs, dp);
        }

        ImGui.Dummy(new Vector2(totalWidth, rowHeight));
        ImGui.EndChild();

        var parentDrawList = ImGui.GetWindowDrawList();

        // Draw playhead line using the parent draw list so it renders above the scrolling content
        if (isReplaying)
        {
            var lineX = childPos.X + playheadOffset;
            var lineTop = childPos.Y;
            var lineBottom = childPos.Y + contentHeight;
            parentDrawList.AddLine(new Vector2(lineX, lineTop), new Vector2(lineX, lineBottom), PlayheadColor, 2f);
            parentDrawList.AddTriangleFilled(
                new Vector2(lineX - 5f, lineTop),
                new Vector2(lineX + 5f, lineTop),
                new Vector2(lineX, lineTop + 8f),
                PlayheadColor);
        }

    }

    private void RebuildRulerLabels(uint maxTimestamp, int intervalSeconds)
    {
        cachedRulerLabels.Clear();
        foreach (var negTime in PreStartMarkersMs)
            cachedRulerLabels[negTime] = "-" + TimeSpan.FromMilliseconds(-negTime).ToString(@"mm\:ss");
        var stepMs = (long)(Math.Max(1, intervalSeconds) * 1000);
        for (long t = 0; t <= maxTimestamp; t += stepMs)
            cachedRulerLabels[t] = TimeSpan.FromMilliseconds(t).ToString(@"mm\:ss");
        cachedRulerLabelMaxTimestamp    = maxTimestamp;
        cachedRulerLabelIntervalSeconds = intervalSeconds;
    }

    private void DrawTimelineRuler(uint maxTimestamp, float totalWidth, float leadInMs, float cullLeft, float cullRight)
    {
        var settings = plugin.Configuration.TimelineWindow;

        if (maxTimestamp != cachedRulerLabelMaxTimestamp ||
            settings.RulerIntervalSeconds != cachedRulerLabelIntervalSeconds)
            RebuildRulerLabels(maxTimestamp, settings.RulerIntervalSeconds);

        var drawList = ImGui.GetWindowDrawList();
        var origin   = ImGui.GetCursorScreenPos();
        var scale    = settings.TimelineScale;

        drawList.AddRectFilled(origin, origin + new Vector2(totalWidth, RulerHeight), RulerBgColor);

        var timeStepMs = (long)(Math.Max(1, settings.RulerIntervalSeconds) * 1000);
        var tickColor  = ImGui.GetColorU32(ImGuiCol.Text);

        // Fixed pre-start markers — only drawn when the lead-in extends far enough to include them.
        foreach (var negTime in PreStartMarkersMs)
        {
            if (-negTime >= (long)leadInMs) continue;
            var x = origin.X + ((negTime + leadInMs) * scale / 10f);
            if (x < cullLeft - 50f || x > cullRight + 50f) continue;
            drawList.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y + RulerHeight), tickColor);
            drawList.AddText(new Vector2(x + 2, origin.Y + 2), tickColor, cachedRulerLabels[negTime]);
        }

        // Positive-time markings from t=0 to end of fight.
        for (long time = 0; time <= maxTimestamp; time += timeStepMs)
        {
            var x = origin.X + ((time + leadInMs) * scale / 10f);
            if (x < cullLeft - 50f || x > cullRight + 50f) continue;
            drawList.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y + RulerHeight), tickColor);
            drawList.AddText(new Vector2(x + 2, origin.Y + 2), tickColor, cachedRulerLabels[time]);
        }

        ImGui.Dummy(new Vector2(totalWidth, RulerHeight));
    }

    private void DrawCastEvent(CastLogEntry log, ImDrawListPtr drawList, Vector2 rowTop, float lanePadding, float leadInMs, in DrawParams dp)
    {
        // For precast events, Timestamp is the cast completion. Shift the icon left to the
        // inferred cast-start so it renders identically to any other timed cast.
        var rawX = rowTop.X + ((log.Timestamp + leadInMs) * dp.Scale / 10f);
        var x = log.IsPrecast ? rawX - (float)(log.CastTime * dp.Scale * 100f) : rawX;

        // GCDs sit in the lower lane; oGCDs/abilities in the upper lane at reduced size.
        var gcdY = rowTop.Y + dp.OGcdSize + lanePadding;
        bool isGcd = log.IsGcd;
        var drawSize = isGcd ? dp.IconSize : dp.OGcdSize;
        var drawY = isGcd ? gcdY : rowTop.Y;

        var iconMin = new Vector2(x, drawY);
        var iconMax = new Vector2(x + drawSize, drawY + drawSize);

        // Cast duration trail: starts at icon right edge, height is 35% of drawSize, centered.
        // icon width + trail width = total cast time in pixels.
        if (!log.IsInstant && log.CastTime > 0)
        {
            var castWidthPx = (float)(log.CastTime * dp.Scale * 100f);
            var trailWidth = Math.Max(0f, castWidthPx - drawSize);
            if (trailWidth > 0)
            {
                var trailHeight = drawSize * 0.35f;
                var trailY = drawY + (drawSize - trailHeight) * 0.5f;
                var trailColor = dp.UseCustomTrailColor ? dp.CustomTrailColor : log.CachedTrailColor;
                drawList.AddRectFilled(
                    new Vector2(x + drawSize, trailY),
                    new Vector2(x + castWidthPx, trailY + trailHeight),
                    trailColor);
            }
        }

        // Ability icon, falling back to a job-colored rectangle.
        // IconTextureCache is populated at import time so no per-frame GetFromGameIcon call is needed.
        var jobRect = true;
        if (log.CachedIconId > 0 && IconTextureCache.TryGetValue(log.CachedIconId, out var tex))
        {
            var wrap = tex.GetWrapOrEmpty();
            if (wrap.Handle != nint.Zero)
            {
                drawList.AddImage(wrap.Handle, iconMin, iconMax);
                jobRect = false;
            }
        }
        if (jobRect)
            drawList.AddRectFilled(iconMin, iconMax, JobUtilities.GetJobColor(log.SourceJobId));

        // Ability type label overlaid at the bottom of the icon
        if (dp.ShowIconLabels && !string.IsNullOrEmpty(log.AbilityType))
        {
            var fontSize = ImGui.GetFontSize();
            var labelY = drawY + drawSize - fontSize;
            var labelPos = new Vector2(x + 1, labelY);
            if (!AbilityTypeSizeCache.TryGetValue(log.AbilityType, out var labelSize))
            {
                labelSize = ImGui.CalcTextSize(log.AbilityType);
                AbilityTypeSizeCache[log.AbilityType] = labelSize;
            }
            drawList.AddRectFilled(
                labelPos,
                new Vector2(x + labelSize.X + 2, labelY + fontSize),
                LabelBgColor);
            drawList.AddText(labelPos, LabelTextColor, log.AbilityType);
        }

        // Tooltip — hit area spans the full cast time width (icon + trail).
        // WindowHovered is checked first (one bool read) to skip float maths when the
        // mouse is outside the timeline child window entirely.
        var totalCastWidthPx = !log.IsInstant && log.CastTime > 0
            ? Math.Max(drawSize, (float)(log.CastTime * dp.Scale * 100f))
            : drawSize;
        if (dp.WindowHovered &&
            dp.MousePos.X >= x && dp.MousePos.X <= x + totalCastWidthPx &&
            dp.MousePos.Y >= drawY && dp.MousePos.Y <= drawY + drawSize)
        {
            var typeLabel = isGcd ? "GCD" : "Ability";
            var castDesc = log.IsInstant ? "Instant" : $"{log.CastTime:F2}s cast";
            ImGui.SetTooltip($"{log.AbilityName} [{typeLabel}]\n{castDesc}");
        }
    }

    // Called only at import/selection time (SetPlayerCastData), never per frame.
    private uint GetAbilityIconId(uint abilityId, string abilityName)
    {
        uint iconId = 0;

        // AbilityId=0 means unresolved — skip the Action sheet (row 0 is a placeholder).
        if (abilityId > 0)
        {
            try
            {
                // Use the Experimental sheet: it tracks the "latest" EXDSchema branch, which stays
                // current with game patches. The stable Action sheet is pinned to a fixed schema
                // version and can fall behind after content patches, causing Icon to read from the
                // wrong column offset and return 0 for valid abilities.
#pragma warning disable PendingExcelSchema
                var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.Action>();
                if (sheet != null && sheet.HasRow(abilityId))
                    iconId = sheet.GetRow(abilityId).Icon;
#pragma warning restore PendingExcelSchema
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"Icon lookup failed for ability {abilityId}: {ex.Message}");
            }
        }

        // Potions/tinctures are items, not actions — their IDs don't map to the Action sheet.
        // "Tincture" is the generic CSV label; resolve it to a specific item for icon lookup.
        if (iconId == 0 && !string.IsNullOrEmpty(abilityName))
        {
            var itemName = abilityName.Equals("Tincture", StringComparison.OrdinalIgnoreCase)
                ? "Grade 4 Tincture of Intelligence"
                : abilityName;
            iconId = GetItemIconIdByName(itemName);
        }

        return iconId;
    }

    // Strip FFLogs HQ suffix (" [HQ]") before matching against the Item sheet,
    // which only stores the base item name.
    private static string NormaliseName(string s)
    {
        const string hqSuffix = " [HQ]";
        if (s.EndsWith(hqSuffix, StringComparison.OrdinalIgnoreCase))
            s = s.Substring(0, s.Length - hqSuffix.Length);
        return s.Trim();
    }

    private static uint GetItemIconIdByName(string name)
    {
        try
        {
            if (ItemIconByName == null)
            {
                var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                if (sheet == null) return 0;
                var index = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in sheet)
                {
                    var rowName = row.Name.ToString();
                    if (!string.IsNullOrEmpty(rowName))
                        index.TryAdd(NormaliseName(rowName), row.Icon);
                }
                ItemIconByName = index;
            }

            return ItemIconByName.TryGetValue(NormaliseName(name), out var icon) ? icon : 0;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"Item icon lookup failed for '{name}': {ex.Message}");
        }
        return 0;
    }

    private void DrawContextMenu()
    {
        if (!ImGui.BeginPopupContextWindow("##timeline_ctx"))
            return;

        ImGui.Text("Select Timeline");
        ImGui.Separator();

        var playerCasts = plugin.Configuration.ImportedPlayerCasts;

        if (playerCasts.Count == 0)
        {
            ImGui.TextDisabled("No saved timelines.");
        }
        else
        {
            if (ImGui.Selectable("(None)", currentPlayerCastData == null))
                SetPlayerCastData(null);

            for (int i = 0; i < playerCasts.Count; i++)
            {
                var pcd = playerCasts[i];
                var playerName = string.IsNullOrEmpty(pcd.PlayerInfo.Name) ? "(Unknown)" : pcd.PlayerInfo.Name;
                var jobName = string.IsNullOrEmpty(pcd.PlayerInfo.JobName) ? "" : $" ({pcd.PlayerInfo.JobName})";
                var fightName = string.IsNullOrEmpty(pcd.FightInfo.Name) ? "" : $" \u2014 {pcd.FightInfo.Name}";
                var label = $"{playerName}{jobName}{fightName}";

                if (ImGui.Selectable(label, currentPlayerCastData == pcd))
                    SetPlayerCastData(pcd);
            }
        }

        ImGui.EndPopup();
    }

    private void DrawLockButton()
    {
        var settings = plugin.Configuration.TimelineWindow;
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var buttonPos = new Vector2(windowPos.X + windowSize.X - 30, windowPos.Y - 18);

        ImGui.SetCursorScreenPos(buttonPos);

        var lockIcon = settings.IsLocked ? "LOCK" : "UNLOCK";
        var lockColor = settings.IsLocked ? new Vector4(1, 1, 0, 1) : new Vector4(1, 0.5f, 0, 1);

        ImGui.PushStyleColor(ImGuiCol.Text, lockColor);
        if (ImGui.Button(lockIcon, new Vector2(25, 18)))
        {
            settings.IsLocked = !settings.IsLocked;
            plugin.Configuration.Save();
        }
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(settings.IsLocked ? "Click to unlock window" : "Click to lock window");
        }
    }

    public void SetJobFilter(uint jobId)
    {
        selectedJobId = jobId;
        showAllJobs = jobId == 0;
        cachedFilteredLogs = null;
        cachedFilterJobId = uint.MaxValue;
    }

    public void SetPlayerCastData(PlayerCastData? playerCastData)
    {
        currentPlayerCastData = playerCastData;
        cachedFilteredLogs = null;
        cachedFilterJobId = uint.MaxValue;

        if (playerCastData != null)
        {
            RebuildTimestampCache(playerCastData.CastLogs);

            // Populate per-entry caches. Also backfills entries deserialized from old saves
            // where CachedTrailColor/CachedIconId/IsGcd are unset.
            foreach (var log in playerCastData.CastLogs)
            {
                log.CachedIconId = GetAbilityIconId(log.AbilityId, log.AbilityName);
                if (log.CachedTrailColor == 0)
                    log.CachedTrailColor = JobUtilities.GetJobTrailColor(log.SourceJobId);
                // Old saves default IsGcd = true; re-derive from AbilityType for oGCD entries.
                if (log.AbilityType == "1") log.IsGcd = false;

                if (log.CachedIconId > 0 && !IconTextureCache.ContainsKey(log.CachedIconId))
                    IconTextureCache[log.CachedIconId] =
                        Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(log.CachedIconId));
            }
        }
        else
        {
            cachedMaxTimestamp = 0;
            cachedEarliestEffectiveMs = 0;
        }
    }

    private void RebuildTimestampCache(List<CastLogEntry> logs)
    {
        cachedMaxTimestamp = 0;
        cachedEarliestEffectiveMs = float.MaxValue;

        foreach (var log in logs)
        {
            if (log.Timestamp > cachedMaxTimestamp)
                cachedMaxTimestamp = log.Timestamp;

            var effectiveStart = log.IsPrecast
                ? log.Timestamp - (float)(log.CastTime * 1000.0)
                : log.Timestamp;
            if (effectiveStart < cachedEarliestEffectiveMs)
                cachedEarliestEffectiveMs = effectiveStart;
        }

        if (cachedEarliestEffectiveMs == float.MaxValue)
            cachedEarliestEffectiveMs = 0;
    }
}
