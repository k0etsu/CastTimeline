using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using CastTimeline.Extensions;

namespace CastTimeline.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private string ffLogsClientId = string.Empty;
    private string ffLogsClientSecret = string.Empty;
    private string importStatus = string.Empty;

    public ConfigWindow(Plugin plugin) : base("CastTimeline Settings", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(700, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        this.configuration = plugin.Configuration;

        // Initialize input fields with current configuration
        ffLogsClientId = configuration.FFLogsClientId;
        ffLogsClientSecret = configuration.FFLogsClientSecret;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Tab system for better organization
        if (ImGui.BeginTabBar("SettingsTabs"))
        {
            if (ImGui.BeginTabItem("Import/Export"))
            {
                DrawFFLogsSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Timeline Window"))
            {
                DrawTimelineWindowSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Data Management"))
            {
                DrawDataManagement();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.Separator();
        DrawImportStatus();
    }

    private void DrawTimelineWindowSettings()
    {
        ImGui.Text("Timeline Window Appearance");
        ImGui.TextWrapped("Customize the appearance and behavior of the floating timeline window.");

        ImGui.Spacing();

        var timelineSettings = configuration.TimelineWindow;

        // Window lock settings
        ImGui.Text("Window Behavior:");
        var isLocked = timelineSettings.IsLocked;
        if (ImGui.Checkbox("Lock Timeline Window in Place", ref isLocked))
        {
            timelineSettings.IsLocked = isLocked;
            configuration.Save();
        }

        if (!isLocked)
        {
            ImGui.Indent();
            ImGui.Text("When unlocked, the window can be dragged around.");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Background settings
        ImGui.Text("Background Appearance:");

        var bgAlpha = timelineSettings.BackgroundAlpha;
        if (ImGui.SliderFloat("Background Alpha", ref bgAlpha, 0.0f, 1.0f, "%.2f"))
        {
            timelineSettings.BackgroundAlpha = bgAlpha;
            var currentBgColor = timelineSettings.BackgroundColor;
            timelineSettings.BackgroundColor = new Vector4(currentBgColor.X, currentBgColor.Y, currentBgColor.Z, bgAlpha);
            configuration.Save();
        }

        var bgColor = timelineSettings.BackgroundColor;
        var bgColorVector = new Vector3(bgColor.X, bgColor.Y, bgColor.Z);
        if (ImGui.ColorEdit3("Background Color", ref bgColorVector))
        {
            timelineSettings.BackgroundColor = new Vector4(bgColorVector.X, bgColorVector.Y, bgColorVector.Z, bgAlpha);
            configuration.Save();
        }

        ImGui.Spacing();

        // Outline settings
        ImGui.Text("Outline Settings:");

        var showOutline = timelineSettings.ShowOutlineWhenUnlocked;
        if (ImGui.Checkbox("Show Outline When Unlocked", ref showOutline))
        {
            timelineSettings.ShowOutlineWhenUnlocked = showOutline;
            configuration.Save();
        }

        if (showOutline)
        {
            ImGui.Indent();

            var outlineColor = timelineSettings.OutlineColor;
            var outlineColorVector = new Vector3(outlineColor.X, outlineColor.Y, outlineColor.Z);
            if (ImGui.ColorEdit3("Outline Color", ref outlineColorVector))
            {
                timelineSettings.OutlineColor = new Vector4(outlineColorVector.X, outlineColorVector.Y, outlineColorVector.Z, outlineColor.W);
                configuration.Save();
            }

            var outlineThickness = timelineSettings.OutlineThickness;
            if (ImGui.SliderFloat("Outline Thickness", ref outlineThickness, 1.0f, 5.0f, "%.1f"))
            {
                timelineSettings.OutlineThickness = outlineThickness;
                configuration.Save();
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Scale settings
        ImGui.Text("Timeline Scale:");

        var timelineScale = timelineSettings.TimelineScale;
        if (ImGui.SliderFloat("Scale", ref timelineScale, 0.5f, 3.0f, "%.1f"))
        {
            timelineSettings.TimelineScale = timelineScale;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Text("Icon Scale:");

        var iconScale = timelineSettings.IconScale;
        if (ImGui.SliderFloat("Icon Scale", ref iconScale, 0.5f, 4.0f, "%.1f"))
        {
            timelineSettings.IconScale = iconScale;
            configuration.Save();
        }

        ImGui.Spacing();

        // Ruler settings
        ImGui.Text("Ruler:");

        var showRuler = timelineSettings.ShowRuler;
        if (ImGui.Checkbox("Show Ruler", ref showRuler))
        {
            timelineSettings.ShowRuler = showRuler;
            configuration.Save();
        }

        if (showRuler)
        {
            ImGui.Indent();
            var rulerInterval = timelineSettings.RulerIntervalSeconds;
            if (ImGui.SliderInt("Interval (seconds)", ref rulerInterval, 1, 60))
            {
                timelineSettings.RulerIntervalSeconds = rulerInterval;
                configuration.Save();
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Cast trail color
        ImGui.Text("Cast Trail Color:");

        var useCustom = timelineSettings.UseCustomTrailColor;
        if (ImGui.Checkbox("Use custom trail color (default: job color)", ref useCustom))
        {
            timelineSettings.UseCustomTrailColor = useCustom;
            configuration.Save();
        }

        if (useCustom)
        {
            ImGui.Indent();
            var trailColor = timelineSettings.TrailColor;
            if (ImGui.ColorEdit3("Trail Color##trailcol", ref trailColor))
            {
                timelineSettings.TrailColor = trailColor;
                configuration.Save();
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Position settings
        ImGui.Text("Window Position:");

        var rememberPos = timelineSettings.RememberPosition;
        if (ImGui.Checkbox("Remember Window Position", ref rememberPos))
        {
            timelineSettings.RememberPosition = rememberPos;
            configuration.Save();
        }

        if (rememberPos)
        {
            ImGui.Indent();
            ImGui.Text($"Current Position: ({timelineSettings.WindowPosition.X:F0}, {timelineSettings.WindowPosition.Y:F0})");
            ImGui.Text($"Current Size: ({timelineSettings.WindowSize.X:F0} x {timelineSettings.WindowSize.Y:F0})");

            if (ImGui.Button("Reset Position"))
            {
                timelineSettings.WindowPosition = new Vector2(100, 100);
                timelineSettings.WindowSize = new Vector2(800, 300);
                configuration.Save();
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Preview/Open button
        if (ImGui.Button("Open Timeline Window"))
        {
            plugin.ToggleTimelineUi();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset All Timeline Settings"))
        {
            ImGui.OpenPopup("Reset Timeline Settings");
        }

        // Reset confirmation popup
        bool openReset = true;
        if (ImGui.BeginPopupModal("Reset Timeline Settings", ref openReset, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Are you sure you want to reset all timeline window settings?");
            ImGui.Text("This will restore the default appearance and behavior.");

            ImGui.Spacing();

            if (ImGui.Button("Yes, Reset"))
            {
                configuration.TimelineWindow = new TimelineWindowSettings();
                configuration.Save();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawFFLogsSettings()
    {
        // Create a child window with scrollbar for FFLogs settings
        if (ImGui.BeginChild("FFLogsSettingsChild", new Vector2(0, 400), true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            ImGui.Text("FFLogs API Settings");
            ImGui.TextWrapped("Configure your FFLogs OAuth2 client credentials to import cast data directly from FFLogs reports.");

            ImGui.Spacing();

            ImGui.Text("Client Credentials:");

            ImGui.Text("Client ID:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##fflogs_client_id", "Enter your FFLogs v2 Client ID", ref ffLogsClientId, 100);

            ImGui.Text("Client Secret:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##fflogs_client_secret", "Enter your FFLogs v2 Client Secret", ref ffLogsClientSecret, 100);

            if (ImGui.Button("Save Credentials"))
            {
                configuration.FFLogsClientId = ffLogsClientId;
                configuration.FFLogsClientSecret = ffLogsClientSecret;
                configuration.Save();
                Plugin.Log.Information("FFLogs client credentials saved");
            }

            ImGui.SameLine();
            if (ImGui.Button("Get Client Credentials"))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.fflogs.com/api/clients/",
                    UseShellExecute = true
                });
            }

            ImGui.Spacing();

            // Show token status
            ImGui.Text("Access Token Status:");

            if (plugin.FFLogsService.IsTokenValid)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Token is valid and ready for use");

                var statusCode = plugin.FFLogsService.LastTokenStatusCode;
                if (statusCode == System.Net.HttpStatusCode.OK)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Last token request: {statusCode}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Last token request: {statusCode}");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "No valid access token");

                var statusCode = plugin.FFLogsService.LastTokenStatusCode;
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"Last token request: {statusCode}");

                if (ImGui.Button("Refresh Token"))
                {
                    plugin.FFLogsService.SetToken();
                    importStatus = "Token refresh initiated";
                }
            }

            ImGui.Spacing();

            ImGui.EndChild();
        }
    }

    private void DrawDataManagement()
    {
        ImGui.Text("Data Management");

        if (ImGui.Button("Clear All Timelines"))
        {
            ImGui.OpenPopup("Confirm Clear Data");
        }

        bool open = true;
        if (ImGui.BeginPopupModal("Confirm Clear Data", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Are you sure you want to clear all saved timelines?");
            ImGui.Text("This action cannot be undone.");

            ImGui.Spacing();

            if (ImGui.Button("Yes, Clear All"))
            {
                configuration.ImportedPlayerCasts.Clear();
                configuration.Save();
                Plugin.Log.Information("All saved timelines cleared");
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.Spacing();

        ImGui.Text($"Saved Timelines: {configuration.ImportedPlayerCasts.Count}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Saved Cast Timelines");
        ImGui.TextWrapped("Delete individual timelines below.");
        ImGui.Spacing();

        if (configuration.ImportedPlayerCasts.Count == 0)
        {
            ImGui.TextDisabled("No saved timelines.");
        }
        else
        {
            if (ImGui.BeginChild("SavedTimelinesChild", new Vector2(0, 0), true, ImGuiWindowFlags.None))
            {
                int castToDelete = -1;
                for (int i = 0; i < configuration.ImportedPlayerCasts.Count; i++)
                {
                    var pcd = configuration.ImportedPlayerCasts[i];
                    string playerName = string.IsNullOrEmpty(pcd.PlayerInfo.Name) ? "(Unknown Player)" : pcd.PlayerInfo.Name;
                    string jobName = string.IsNullOrEmpty(pcd.PlayerInfo.JobName) ? "" : $" ({pcd.PlayerInfo.JobName})";
                    string fightName = string.IsNullOrEmpty(pcd.FightInfo.Name) ? "" : $" \u2014 {pcd.FightInfo.Name}";
                    string date = pcd.ImportDate.ToString("yyyy-MM-dd");
                    int castCount = pcd.CastLogs.Count;

                    ImGui.Text($"{playerName}{jobName}{fightName}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({date}, {castCount} casts)");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Delete##pcast_{i}"))
                        castToDelete = i;
                }

                if (castToDelete >= 0)
                {
                    var deleted = configuration.ImportedPlayerCasts[castToDelete];
                    configuration.ImportedPlayerCasts.RemoveAt(castToDelete);
                    configuration.Save();
                    Plugin.Log.Information($"Deleted timeline: {deleted.PlayerInfo.Name} / {deleted.FightInfo.Name}");
                }

                ImGui.EndChild();
            }
        }
    }

    private void DrawImportStatus()
    {
        if (!string.IsNullOrEmpty(importStatus))
        {
            ImGuiExt.DrawStatusMessage(importStatus);
            if (ImGui.IsWindowAppearing())
                _ = ClearStatusAsync();
        }
    }

    private async Task ClearStatusAsync()
    {
        await Task.Delay(5000);
        importStatus = string.Empty;
    }
}
