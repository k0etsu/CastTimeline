using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CastTimeline.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("CastTimeline - Fight Manager", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();

        if (ImGui.Button("Open Timeline"))
            plugin.ToggleTimelineUi();

        ImGui.SameLine();

        if (ImGui.Button("Import from FFLogs"))
            ImGui.OpenPopup("FFLogs Import");

        ImGui.SameLine();

        if (ImGui.Button("Import CSV"))
            ImGui.OpenPopup("CSV Import");

        DrawImportPopup();
        DrawCsvImportPopup();

        ImGui.Separator();

        DrawTimelineSelector();

        ImGui.Separator();

        if (availableFights.Count > 0 || isImporting)
        {
            DrawEnhancedImportFlow();
            ImGui.Separator();
        }
    }

    private void DrawTimelineSelector()
    {
        ImGui.Text("Active Timeline");
        ImGui.Spacing();

        var playerCasts = plugin.Configuration.ImportedPlayerCasts;

        if (playerCasts.Count == 0)
        {
            ImGui.TextDisabled("No saved timelines. Import data using the buttons above.");
            return;
        }

        var active = plugin.TimelineWindow.CurrentPlayerCastData;
        var items = new string[playerCasts.Count + 1];
        items[0] = "(None)";
        for (int i = 0; i < playerCasts.Count; i++)
        {
            var pcd = playerCasts[i];
            var playerName = string.IsNullOrEmpty(pcd.PlayerInfo.Name) ? "(Unknown)" : pcd.PlayerInfo.Name;
            var jobName = string.IsNullOrEmpty(pcd.PlayerInfo.JobName) ? "" : $" ({pcd.PlayerInfo.JobName})";
            var fightName = string.IsNullOrEmpty(pcd.FightInfo.Name) ? "" : $" \u2014 {pcd.FightInfo.Name}";
            items[i + 1] = $"{playerName}{jobName}{fightName}";
        }

        int comboIndex = active == null ? 0 : playerCasts.IndexOf(active) + 1;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##timeline_select", ref comboIndex, items, items.Length))
            plugin.TimelineWindow.SetPlayerCastData(comboIndex == 0 ? null : playerCasts[comboIndex - 1]);
    }
}
