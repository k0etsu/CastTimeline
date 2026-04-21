using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace CastTimeline;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // FFLogs OAuth2 credentials — stored in config, never logged
    public string FFLogsClientId { get; set; } = string.Empty;
    public string FFLogsClientSecret { get; set; } = string.Empty;
    public string FFLogsAccessToken { get; set; } = string.Empty;
    public DateTime FFLogsTokenExpiry { get; set; } = DateTime.MinValue;

    public TimelineWindowSettings TimelineWindow { get; set; } = new();

    public List<PlayerCastData> ImportedPlayerCasts { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public class TimelineWindowSettings
{
    public bool IsLocked { get; set; } = true;
    public float BackgroundAlpha { get; set; } = 0.8f;
    public Vector4 BackgroundColor { get; set; } = new(0.1f, 0.1f, 0.1f, 0.8f);
    public Vector4 OutlineColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);
    public float OutlineThickness { get; set; } = 2.0f;
    public bool ShowOutlineWhenUnlocked { get; set; } = true;
    public Vector2 WindowPosition { get; set; } = new(100, 100);
    public Vector2 WindowSize { get; set; } = new(800, 300);
    public bool RememberPosition { get; set; } = true;
    public float TimelineScale { get; set; } = 1.0f;
    public float IconScale { get; set; } = 2.0f;
    public bool ShowRuler { get; set; } = true;
    public int RulerIntervalSeconds { get; set; } = 10;
    public bool UseCustomTrailColor { get; set; } = false;
    // RGB only — alpha is always fixed at 0.6 in the renderer.
    public Vector3 TrailColor { get; set; } = new(1f, 1f, 1f);
}

[Serializable]
public class CastLogEntry
{
    // Milliseconds from fight start (i.e. when the countdown hit zero / pull happened)
    public uint Timestamp { get; set; }

    public string AbilityName { get; set; } = string.Empty;
    public uint AbilityId { get; set; }

    // AbilityType comes from FFLogs masterData ability type field.
    // "1" = oGCD ability/item; anything else is treated as a GCD.
    public string AbilityType { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public uint SourceJobId { get; set; }

    public double CastTime { get; set; }
    public bool IsInstant { get; set; } = false;
    // True when this is the first cast of the fight and the cast began before logging started.
    // The Timestamp marks the cast completion; the trail renders leftward by CastTime.
    public bool IsPrecast { get; set; } = false;
    // Pre-packed ImGui trail color (job color at 0.6f alpha). Set at import time so
    // DrawCastEvent never calls GetJobColorVec4 per frame. Zero means unset (old saves).
    public uint CachedTrailColor { get; set; }
    // Game icon ID resolved at import time so DrawCastEvent never hits Lumina per frame.
    // Zero means unset (old saves) or no icon found.
    public uint CachedIconId { get; set; }
}

[Serializable]
public class FightInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long StartTime { get; set; }
    public long EndTime { get; set; }
    public int EncounterID { get; set; }
    public bool? Kill { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public DateTime FightDate { get; set; }
    public TimeSpan Duration => TimeSpan.FromMilliseconds(EndTime - StartTime);
    public List<int> FriendlyPlayerIds { get; set; } = new();
    public List<PlayerInfo> Players { get; set; } = new();
}

[Serializable]
public class PlayerInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public uint JobId { get; set; }
    public uint JobColor { get; set; }
    public object? Type { get; set; }
    public string? Server { get; set; }
    public string? SubType { get; set; }
}

[Serializable]
public class PlayerCastData
{
    public string ReportCode { get; set; } = string.Empty;
    public FightInfo FightInfo { get; set; } = new();
    public PlayerInfo PlayerInfo { get; set; } = new();
    public List<CastLogEntry> CastLogs { get; set; } = new();
    public DateTime ImportDate { get; set; } = DateTime.Now;
}
