using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using CastTimeline.Extensions;
using static CastTimeline.Configuration;

namespace CastTimeline.Windows;

// Partial class containing all import-flow state and UI for MainWindow.
public partial class MainWindow
{
    private string ffLogsReportUrl = string.Empty;
    private string ffLogsReportCode = string.Empty;

    private List<FightInfo> availableFights = new();
    private FightInfo? selectedFight;
    private PlayerInfo? selectedPlayer;
    private int importStep = 0; // 0=initial, 1=fight selection, 2=player selection, 3=complete
    private bool isImporting = false;
    private string importStatus = string.Empty;

    // Popup triggered by the "Import from FFLogs" button in Draw().
    private void DrawImportPopup()
    {
        if (!ImGui.BeginPopup("FFLogs Import"))
            return;

        ImGui.Text("Import from FFLogs");
        ImGui.Separator();
        ImGui.TextWrapped("Enter FFLogs report URL to fetch available fights:");
        ImGui.Spacing();

        ImGui.InputTextWithHint("##fflogs_url", "https://www.fflogs.com/reports/...", ref ffLogsReportUrl, 200);
        ImGui.Spacing();

        var currentReportCode = ExtractReportCodeFromUrl(ffLogsReportUrl);

        if (ImGui.Button("Fetch Fights"))
        {
            if (!string.IsNullOrEmpty(currentReportCode))
            {
                ffLogsReportCode = currentReportCode;
                _ = FetchFightsAsync();
                ImGui.CloseCurrentPopup();
            }
            else
            {
                importStatus = "Error: Please enter a valid FFLogs URL";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.Spacing();

        if (!string.IsNullOrEmpty(currentReportCode))
            ImGui.Text($"Report Code: {currentReportCode}");
        else if (!string.IsNullOrEmpty(ffLogsReportUrl))
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Please enter a valid FFLogs URL");
        else
            ImGui.Text("Enter a FFLogs URL to begin import");

        ImGui.EndPopup();
    }

    private void DrawEnhancedImportFlow()
    {
        switch (importStep)
        {
            case 0: DrawInitialImportStep(); break;
            case 1: DrawFightSelectionStep(); break;
            case 2: DrawPlayerSelectionStep(); break;
            case 3: DrawImportCompleteStep(); break;
        }

        if (selectedFight != null && importStep >= 1)
        {
            ImGui.Separator();
            DrawFightInformation();
        }

        if (!string.IsNullOrEmpty(importStatus))
            ImGuiExt.DrawStatusMessage(importStatus);
    }

    private void DrawInitialImportStep()
    {
        ImGui.Text("Step 1: Enter FFLogs Report URL");
        ImGui.TextWrapped("Enter the URL of the FFLogs report you want to import from.");
        ImGui.Spacing();

        ImGui.InputTextWithHint("##fflogs_url", "https://www.fflogs.com/reports/...", ref ffLogsReportUrl, 200);
        ImGui.Spacing();

        var currentReportCode = ExtractReportCodeFromUrl(ffLogsReportUrl);

        if (ImGui.Button("Fetch Fights"))
        {
            if (!string.IsNullOrEmpty(currentReportCode))
            {
                ffLogsReportCode = currentReportCode;
                _ = FetchFightsAsync();
            }
            else
            {
                importStatus = "Error: Please enter a valid FFLogs URL";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            ResetImportFlow();
            ImGui.CloseCurrentPopup();
        }

        ImGui.Spacing();

        if (!string.IsNullOrEmpty(currentReportCode))
            ImGui.Text($"Report Code: {currentReportCode}");
        else if (!string.IsNullOrEmpty(ffLogsReportUrl))
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Please enter a valid FFLogs URL");
        else
            ImGui.Text("Enter a FFLogs URL to begin import");
    }

    private void DrawFightSelectionStep()
    {
        ImGui.Text("Select Fight from Report:");
        ImGui.TextWrapped($"Found {availableFights.Count} fights. Select one to view players.");
        ImGui.Spacing();

        if (ImGui.BeginChild("FightSelectionChild", new Vector2(0, 200), true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            for (int i = 0; i < availableFights.Count; i++)
            {
                var fight = availableFights[i];
                var isSelected = selectedFight?.Id == fight.Id;
                var killColor = fight.Kill == true ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0.5f, 0, 1);
                var label = $"{fight.Name} - {fight.ZoneName} ({fight.Duration:mm\\:ss})";

                if (ImGui.Selectable(label, isSelected))
                    selectedFight = fight;

                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - 30);
                ImGui.TextColored(killColor, fight.Kill == true ? "KILL" : "WIPE");
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();

        ImGui.SameLine();
        if (ImGui.Button("Clear Import"))
            ResetImportFlow();

        if (selectedFight == null)
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Select a fight to continue");
    }

    private void DrawPlayerSelectionStep()
    {
        if (selectedFight == null) return;

        ImGui.Text("Select Player from Fight:");
        ImGui.TextWrapped($"Found {selectedFight.Players.Count} players in {selectedFight.Name}. Select one to import cast events.");
        ImGui.Spacing();

        if (ImGui.BeginChild("PlayerSelectionChild", new Vector2(0, 180), true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            foreach (var player in selectedFight.Players.OrderBy(p => p.Name))
            {
                var isSelected = selectedPlayer?.Id == player.Id;

                ImGui.TextColored(player.JobColor, player.JobName);
                ImGui.SameLine();
                if (ImGui.Selectable(player.Name, isSelected))
                    selectedPlayer = player;
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();

        if (selectedPlayer != null)
        {
            if (ImGui.Button("Import Cast Events"))
                _ = ImportPlayerCastEventsAsync();
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Select a player to import");
        }
    }

    private void DrawImportCompleteStep()
    {
        ImGui.Text("✓ Import Complete!");

        if (selectedPlayer != null && selectedFight != null)
        {
            ImGui.Text($"Imported {selectedPlayer.Name} ({selectedPlayer.JobName})");
            ImGui.Text($"From: {selectedFight.Name} - {selectedFight.ZoneName}");
        }

        ImGui.Spacing();

        if (ImGui.Button("View Timeline"))
        {
            var importedData = plugin.Configuration.ImportedPlayerCasts
                .FirstOrDefault(p => p.PlayerInfo.Id == selectedPlayer?.Id && p.FightInfo.Id == selectedFight?.Id);

            if (importedData != null)
            {
                plugin.TimelineWindow.SetPlayerCastData(importedData);
                plugin.ToggleTimelineUi();
            }

            ResetImportFlow();
        }

        ImGui.SameLine();
        if (ImGui.Button("Import Another Timeline"))
        {
            importStep = 1;
            selectedPlayer = null;
            selectedFight = null;
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Import"))
            ResetImportFlow();
    }

    private void DrawFightInformation()
    {
        if (selectedFight == null || importStep != 1) return;

        ImGui.Text("Fight Information:");
        ImGui.Separator();

        ImGui.Text($"Name: {selectedFight.Name}");
        ImGui.Text($"Zone: {selectedFight.ZoneName}");
        ImGui.Text($"Date: {selectedFight.FightDate:yyyy-MM-dd HH:mm:ss}");
        ImGui.Text($"Duration: {selectedFight.Duration:mm\\:ss}");

        ImGui.Spacing();
        ImGui.Text("Players:");

        if (selectedFight.Players.Count > 0)
            DrawPlayerSelectionStep();
        else if (selectedFight.FriendlyPlayerIds.Count > 0)
            DrawPlayerRosterFallback();
        else
            ImGui.Text("No player data available for this fight.");
    }

    private void DrawPlayerRosterFallback()
    {
        if (selectedFight == null) return;

        if (ImGui.BeginChild("FightPlayerRoster", new Vector2(0, 150), true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (ImGui.BeginTable("PlayerTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 80f);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60f);
                ImGui.TableHeadersRow();

                foreach (var player in selectedFight.FriendlyPlayerIds)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text($"Player {player}");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text("N/A");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.Text($"{player}");
                }
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private void ResetImportFlow()
    {
        importStep = 0;
        availableFights.Clear();
        selectedFight = null;
        selectedPlayer = null;
        ffLogsReportUrl = string.Empty;
        ffLogsReportCode = string.Empty;
        importStatus = string.Empty;
    }

    private string ExtractReportCodeFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        try
        {
            var uri = new Uri(url);
            var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < pathSegments.Length - 1; i++)
            {
                if (pathSegments[i].Equals("reports", StringComparison.OrdinalIgnoreCase) && i + 1 < pathSegments.Length)
                    return pathSegments[i + 1];
            }
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private async Task FetchFightsAsync()
    {
        if (isImporting) return;

        isImporting = true;
        importStatus = "Fetching fights...";

        try
        {
            var fights = await plugin.FFLogsService.GetReportFightsAsync(ffLogsReportCode);
            if (fights != null)
            {
                availableFights = fights;
                importStep = 1;
                importStatus = $"Found {fights.Count} fights";
                Plugin.Log.Information($"Fetched {fights.Count} fights from report {ffLogsReportCode}");
            }
            else
            {
                importStatus = "Failed to fetch fights - check logs for details";
            }
        }
        catch (Exception ex)
        {
            importStatus = $"Error fetching fights: {ex.Message}";
            Plugin.Log.Error($"FFLogs fight fetch error: {ex.Message}");
        }
        finally
        {
            isImporting = false;
        }
    }

    private async Task ImportPlayerCastEventsAsync()
    {
        if (isImporting || selectedFight == null || selectedPlayer == null) return;

        isImporting = true;
        importStatus = "Importing cast events...";

        try
        {
            var playerCastData = await plugin.FFLogsService.GetPlayerCastEventsAsync(
                ffLogsReportCode,
                selectedFight.Id,
                selectedPlayer.Id,
                selectedFight,
                selectedPlayer);

            if (playerCastData != null)
            {
                plugin.Configuration.ImportedPlayerCasts.Add(playerCastData);
                plugin.Configuration.Save();

                importStep = 3;
                importStatus = $"Imported {playerCastData.CastLogs.Count} cast events";
                Plugin.Log.Information($"Imported {playerCastData.CastLogs.Count} cast events for {selectedPlayer.Name}");
            }
            else
            {
                importStatus = "Failed to import cast events - check logs for details";
            }
        }
        catch (Exception ex)
        {
            importStatus = $"Error importing cast events: {ex.Message}";
            Plugin.Log.Error($"FFLogs cast events import error: {ex.Message}");
        }
        finally
        {
            isImporting = false;
        }
    }

    // -------------------------------------------------------------------------
    // CSV import
    // -------------------------------------------------------------------------
    // Expected CSV format (header row required):
    //   time,action,isGCD,castTime
    //
    //   time     — seconds from pull; negative values are precast (clamped to 0 ms)
    //   action   — ability name string OR numeric game ability ID
    //   isGCD    — 1 = GCD spell/weaponskill, 0 = oGCD ability/item
    //   castTime — cast bar length in seconds; 0 = instant
    //
    // When action is a numeric ID the ability name is resolved from the Action
    // Excel sheet (same Lumina path used by the icon cache).
    // -------------------------------------------------------------------------

    private string csvFilePath = string.Empty;
    private string csvPlayerName = "Player";
    private string csvImportStatus = string.Empty;

    private string fileBrowserCurrentDir = string.Empty;
    private string fileBrowserSelectedEntry = string.Empty;
    private List<(string Path, string Name, bool IsDir)> fileBrowserEntries = new();
    private bool fileBrowserNeedsRefresh = true;

    private void DrawCsvImportPopup()
    {
        if (!ImGui.BeginPopup("CSV Import"))
            return;

        DrawFileBrowserModal();

        ImGui.Text("Import from CSV File");
        ImGui.Separator();
        ImGui.TextWrapped("CSV must have a header row: time, action, isGCD, castTime");
        ImGui.Spacing();

        ImGui.Text("File path:");
        ImGui.SetNextItemWidth(340);
        ImGui.InputTextWithHint("##csv_path", "C:\\path\\to\\timeline.csv", ref csvFilePath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse..."))
        {
            var startDir = string.Empty;
            if (!string.IsNullOrEmpty(csvFilePath))
                startDir = Path.GetDirectoryName(csvFilePath) ?? string.Empty;
            if (!Directory.Exists(startDir))
                startDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            fileBrowserCurrentDir = startDir;
            fileBrowserSelectedEntry = string.Empty;
            fileBrowserNeedsRefresh = true;
            ImGui.OpenPopup("##csv_browser");
        }

        ImGui.Spacing();
        ImGui.Text("Player name (shown in tooltip):");
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("##csv_player", ref csvPlayerName, 64);

        ImGui.Spacing();

        if (ImGui.Button("Import"))
        {
            var result = ImportCsvTimeline(csvFilePath, csvPlayerName);
            csvImportStatus = result;
            if (!result.StartsWith("Error"))
            {
                csvFilePath = string.Empty;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel"))
        {
            csvFilePath = string.Empty;
            csvImportStatus = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        if (!string.IsNullOrEmpty(csvImportStatus))
            ImGuiExt.DrawStatusMessage(csvImportStatus);

        ImGui.EndPopup();
    }

    private string ImportCsvTimeline(string filePath, string playerName)
    {
        try
        {
            var (entries, error) = ParseCsvTimeline(filePath);
            if (error != null)
                return $"Error: {error}";

            if (entries.Count == 0)
                return "Error: no valid rows found in file";

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var durationMs = (long)entries.Max(e => e.Timestamp);

            var playerCastData = new PlayerCastData
            {
                ReportCode = $"csv:{fileName}",
                PlayerInfo = new PlayerInfo { Name = playerName, JobName = "CSV" },
                FightInfo = new FightInfo
                {
                    Name = fileName,
                    ZoneName = "CSV Import",
                    StartTime = 0,
                    EndTime = durationMs,
                    FightDate = DateTime.Now,
                },
                CastLogs = entries,
                ImportDate = DateTime.Now,
            };

            plugin.Configuration.ImportedPlayerCasts.Add(playerCastData);
            plugin.TimelineWindow.SetPlayerCastData(playerCastData);
            plugin.Configuration.Save();

            Plugin.Log.Information($"CSV import: {entries.Count} events from '{filePath}'");
            return $"Imported {entries.Count} cast events";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"CSV import failed: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    // Returns (entries, errorMessage). errorMessage is null on success.
    private (List<CastLogEntry> entries, string? error) ParseCsvTimeline(string filePath)
    {
        if (!File.Exists(filePath))
            return (new List<CastLogEntry>(), "file not found");

        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 2)
            return (new List<CastLogEntry>(), "file is empty or has no data rows");

        var entries = new List<CastLogEntry>();

        // Start at line 1 — line 0 is the header
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Limit split to 4 parts so ability names containing commas are preserved
            var parts = line.Split(',', 4);
            if (parts.Length < 4) continue;

            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double timeSec))
                continue;
            if (!int.TryParse(parts[2].Trim(), out int isGcd))
                continue;
            if (!double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double castTime))
                continue;

            var actionRaw = parts[1].Trim();

            // Numeric action column → game ability ID; resolve name from Lumina.
            // String action column → reverse-lookup the ID from the Action sheet so icons work
            // for abilities whose names match exactly (e.g. "High Thunder", "Triplecast").
            // Abilities named with Arabic numerals (e.g. "Fire 4" vs in-game "Fire IV") won't
            // match and will stay at AbilityId=0, showing the job-colour rectangle instead.
            uint abilityId = 0;
            string abilityName = actionRaw;
            if (uint.TryParse(actionRaw, out uint parsedId))
            {
                abilityId = parsedId;
                abilityName = ResolveAbilityName(parsedId);
            }
            else
            {
                abilityId = ResolveAbilityIdFromName(actionRaw);
            }

            // CSV isGCD=0 means oGCD, which maps to AbilityType "1" in our model.
            // CSV isGCD=1 means GCD spell/weaponskill → AbilityType "0" (anything not "1").
            var abilityType = isGcd == 0 ? "1" : "0";

            // Negative time = prepull cast that started before the pull. The renderer
            // expects Timestamp to be the cast completion time for IsPrecast entries,
            // with the icon shifted left by CastTime during drawing.
            bool isPrecast = timeSec < 0 && castTime > 0;
            uint timestampMs = isPrecast
                ? (uint)Math.Max(0.0, (timeSec + castTime) * 1000.0)
                : (uint)(timeSec * 1000.0);

            entries.Add(new CastLogEntry
            {
                Timestamp        = timestampMs,
                AbilityName      = abilityName,
                AbilityId        = abilityId,
                AbilityType      = abilityType,
                SourceName       = csvPlayerName,
                SourceJobId      = 0,
                CastTime         = castTime,
                IsInstant        = castTime == 0,
                IsPrecast        = isPrecast,
                CachedTrailColor = CastTimeline.Utilities.JobUtilities.GetJobTrailColor(0),
            });
        }

        return (entries, null);
    }

    private void DrawFileBrowserModal()
    {
        ImGui.SetNextWindowSize(new Vector2(600, 420), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("##csv_browser", ImGuiWindowFlags.NoTitleBar))
            return;

        if (fileBrowserNeedsRefresh)
        {
            fileBrowserEntries.Clear();
            try
            {
                foreach (var dir in Directory.GetDirectories(fileBrowserCurrentDir).OrderBy(d => d))
                    fileBrowserEntries.Add((dir, Path.GetFileName(dir), true));
                foreach (var file in Directory.GetFiles(fileBrowserCurrentDir, "*.csv").OrderBy(f => f))
                    fileBrowserEntries.Add((file, Path.GetFileName(file), false));
            }
            catch { }
            fileBrowserNeedsRefresh = false;
        }

        // Path bar
        var parent = Directory.GetParent(fileBrowserCurrentDir)?.FullName;
        if (ImGui.Button("Up") && parent != null)
        {
            fileBrowserCurrentDir = parent;
            fileBrowserSelectedEntry = string.Empty;
            fileBrowserNeedsRefresh = true;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(fileBrowserCurrentDir);

        ImGui.Separator();

        // File listing
        var footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeight() + 8;
        if (ImGui.BeginChild("##browser_list", new Vector2(0, -footerHeight), true))
        {
            foreach (var (path, name, isDir) in fileBrowserEntries)
            {
                bool isSelected = fileBrowserSelectedEntry == path;
                if (isDir)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));

                if (ImGui.Selectable(isDir ? $"[+] {name}" : name, isSelected, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    fileBrowserSelectedEntry = path;
                    if (isDir && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        fileBrowserCurrentDir = path;
                        fileBrowserSelectedEntry = string.Empty;
                        fileBrowserNeedsRefresh = true;
                    }
                }

                if (isDir)
                    ImGui.PopStyleColor();
            }
        }
        ImGui.EndChild();

        // Selected file name display
        bool isFileSelected = !string.IsNullOrEmpty(fileBrowserSelectedEntry)
            && fileBrowserEntries.Any(e => e.Path == fileBrowserSelectedEntry && !e.IsDir);
        ImGui.Text(isFileSelected ? $"File: {Path.GetFileName(fileBrowserSelectedEntry)}" : "No file selected");

        if (!isFileSelected) ImGui.BeginDisabled();
        if (ImGui.Button("Select"))
        {
            csvFilePath = fileBrowserSelectedEntry;
            ImGui.CloseCurrentPopup();
        }
        if (!isFileSelected) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel##browser"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    // Built once on the first CSV import that needs a name→ID reverse lookup.
    private static Dictionary<string, uint>? ActionIdByName;

    // Reverse lookup: find the game ability ID whose name matches the given string.
    // Returns 0 if no exact (case-insensitive) match is found.
    private static uint ResolveAbilityIdFromName(string name)
    {
        try
        {
            if (ActionIdByName == null)
            {
#pragma warning disable PendingExcelSchema
                var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.Action>();
                if (sheet == null) return 0;
                var index = new Dictionary<string, uint>();
                foreach (var row in sheet)
                {
                    var rowName = row.Name.ToString();
                    if (!string.IsNullOrEmpty(rowName))
                        index.TryAdd(rowName.ToLowerInvariant(), row.RowId);
                }
#pragma warning restore PendingExcelSchema
                ActionIdByName = index;
            }

            return ActionIdByName.TryGetValue(name.ToLowerInvariant(), out var id) ? id : 0;
        }
        catch { }
        return 0;
    }

    // Look up the localised ability name from the Action Excel sheet.
    // Falls back to "Action <id>" if the row doesn't exist or the sheet is unavailable.
    private static string ResolveAbilityName(uint abilityId)
    {
        try
        {
#pragma warning disable PendingExcelSchema
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.Action>();
            if (sheet != null && sheet.HasRow(abilityId))
            {
                var name = sheet.GetRow(abilityId).Name.ToString();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
#pragma warning restore PendingExcelSchema
        }
        catch { }
        return $"Action {abilityId}";
    }
}
