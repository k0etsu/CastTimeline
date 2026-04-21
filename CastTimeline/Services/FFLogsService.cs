using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static CastTimeline.Configuration;
using CastTimeline.Utilities;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CastTimeline.Services;

public class FFLogsService
{
    private readonly IPluginLog log;
    private readonly HttpClient httpClient;
    private readonly IDalamudPluginInterface pluginInterface;
    private const string FFLogsApiBaseUrl = "https://www.fflogs.com/api/v2";
    private const string FFLogsTokenUrl = "https://www.fflogs.com/oauth/token";

    private static readonly Dictionary<uint, double> ActionCastTimeCache = new();

    public bool IsTokenValid { get; private set; }
    public HttpStatusCode LastTokenStatusCode { get; private set; } = HttpStatusCode.OK;

    public FFLogsService(IPluginLog log, IDalamudPluginInterface pluginInterface)
    {
        this.log = log;
        this.pluginInterface = pluginInterface;
        this.httpClient = new HttpClient();
        this.SetToken();
    }

    public static bool IsConfigSet(IDalamudPluginInterface pluginInterface)
    {
        var config = pluginInterface.GetPluginConfig() as Configuration;
        return config != null &&
               !string.IsNullOrEmpty(config.FFLogsClientId) &&
               !string.IsNullOrEmpty(config.FFLogsClientSecret);
    }

    public void SetToken()
    {
        IsTokenValid = false;
        LastTokenStatusCode = HttpStatusCode.OK;

        if (!IsConfigSet(pluginInterface))
        {
            log.Error("FFLogs client credentials are not configured");
            return;
        }

        Task.Run(async () =>
        {
            var token = await FetchTokenAsync();

            if (token != null && token.Error == null && !string.IsNullOrEmpty(token.AccessToken))
            {
                this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                IsTokenValid = true;
                log.Information("FFLogs access token obtained successfully");
            }
            else
            {
                log.Error($"FFLogs token couldn't be set: {(token == null ? "return was null" : token.Error)}");
            }
        });
    }

    private async Task<OAuth2TokenResponse?> FetchTokenAsync()
    {
        var config = pluginInterface.GetPluginConfig() as Configuration;
        if (config == null) return null;

        using var client = new HttpClient();

        var form = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", config.FFLogsClientId },
            { "client_secret", config.FFLogsClientSecret },
        };

        string? jsonContent = null;

        try
        {
            var tokenResponse = await client.PostAsync(FFLogsTokenUrl, new FormUrlEncodedContent(form));
            LastTokenStatusCode = tokenResponse.StatusCode;

            if (tokenResponse.IsSuccessStatusCode)
            {
                jsonContent = await tokenResponse.Content.ReadAsStringAsync();
                var token = JsonSerializer.Deserialize<OAuth2TokenResponse>(jsonContent, JsonOptions.CaseInsensitive);

                if (token?.AccessToken != null)
                {
                    config.FFLogsAccessToken = token.AccessToken;
                    config.FFLogsTokenExpiry = DateTime.Now.AddSeconds(token.ExpiresIn - 300); // 5 minute buffer
                    config.Save();
                }

                return token;
            }

            log.Error($"Failure status code while fetching token: {tokenResponse.StatusCode} ({(int)tokenResponse.StatusCode})");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Error while fetching FFLogs token.");
            if (jsonContent != null)
                log.Error($"Json content from request: {jsonContent}");
        }

        return null;
    }

    public async Task<List<FightInfo>?> GetReportFightsAsync(string reportCode)
    {
        try
        {
            if (!IsTokenValid)
            {
                log.Error("FFLogs token not valid");
                return null;
            }

            var reportRequest = new
            {
                query = FFLogsQueries.ReportFightsQuery,
                variables = new { code = reportCode }
            };

            var reportResponse = await httpClient.PostAsync($"{FFLogsApiBaseUrl}/client",
                new StringContent(JsonSerializer.Serialize(reportRequest), Encoding.UTF8, "application/json"));

            if (!reportResponse.IsSuccessStatusCode)
            {
                log.Error($"Failed to fetch FFLogs report: {reportResponse.StatusCode}");
                return null;
            }

            var reportJson = await reportResponse.Content.ReadAsStringAsync();

            var reportData = JsonSerializer.Deserialize<FFLogsV2Response>(reportJson, JsonOptions.CaseInsensitive);

            if (reportData?.Data?.ReportData?.Report?.Fights == null)
            {
                log.Error("Invalid FFLogs report data");
                return null;
            }

            var report = reportData.Data.ReportData.Report;
            var fightInfos = new List<FightInfo>();

            foreach (var fight in report.Fights)
            {
                var fightPlayers = new List<PlayerInfo>();
                if (fight.FriendlyPlayers != null && report.MasterData?.Players != null)
                {
                    foreach (var playerId in fight.FriendlyPlayers)
                    {
                        var masterPlayer = report.MasterData.Players.FirstOrDefault(p => p.Id == playerId);
                        if (masterPlayer != null)
                        {
                            if (masterPlayer.Server != null)
                            {
                                string jobName = "UNK";
                                uint jobId = 0;
                                uint jobColor = 0;
                                if (masterPlayer.SubType != null)
                                {
                                    string subType = masterPlayer.SubType.ToString() ?? "UNK";
                                    jobId = JobUtilities.GetJobId(subType);
                                    jobName = JobUtilities.GetJobName(jobId);
                                    jobColor = JobUtilities.GetJobColor(jobId);
                                }

                                fightPlayers.Add(new PlayerInfo
                                {
                                    Id = masterPlayer.Id,
                                    Name = masterPlayer.Name ?? $"Player {masterPlayer.Id}",
                                    JobName = jobName,
                                    JobId = jobId,
                                    JobColor = jobColor,
                                    Type = masterPlayer.Type,
                                    Server = masterPlayer.Server
                                });
                            }
                        }
                        else
                        {
                            fightPlayers.Add(new PlayerInfo
                            {
                                Id = playerId,
                                Name = $"Player {playerId}",
                                JobName = "UNK"
                            });
                        }
                    }
                }

                fightInfos.Add(new FightInfo
                {
                    Id = fight.Id,
                    Name = fight.Name ?? $"Fight {fight.Id}",
                    StartTime = fight.StartTime,
                    EndTime = fight.EndTime,
                    EncounterID = fight.EncounterID,
                    Kill = fight.Kill,
                    ZoneName = report.Zone?.Name ?? "Unknown Zone",
                    FightDate = DateTimeOffset.FromUnixTimeMilliseconds((report.StartTime + fight.StartTime)).DateTime,
                    FriendlyPlayerIds = fight.FriendlyPlayers ?? new List<int>(),
                    Players = fightPlayers
                });
            }

            return fightInfos;
        }
        catch (Exception ex)
        {
            log.Error($"Error fetching report fights: {ex.Message}");
            return null;
        }
    }

    public async Task<PlayerCastData?> GetPlayerCastEventsAsync(string reportCode, int fightId, int playerId, FightInfo fightInfo, PlayerInfo playerInfo)
    {
        try
        {
            if (!IsTokenValid)
            {
                log.Error("FFLogs token not valid");
                return null;
            }

            var fightRequest = new
            {
                query = FFLogsQueries.FightTimesQuery,
                variables = new { code = reportCode, fightID = fightId }
            };

            var fightResponse = await httpClient.PostAsync($"{FFLogsApiBaseUrl}/client",
                new StringContent(JsonSerializer.Serialize(fightRequest), Encoding.UTF8, "application/json"));

            if (!fightResponse.IsSuccessStatusCode)
            {
                log.Error($"Failed to fetch fight times: {fightResponse.StatusCode}");
                return null;
            }

            var fightJson = await fightResponse.Content.ReadAsStringAsync();
            var fightData = JsonSerializer.Deserialize<FFLogsV2Response>(fightJson, JsonOptions.CaseInsensitive);

            var fightDetails = fightData?.Data?.ReportData?.Report?.Fights?.FirstOrDefault();
            if (fightDetails == null)
            {
                log.Error("Fight details not found");
                return null;
            }

            var eventsRequest = new
            {
                query = FFLogsQueries.PlayerEventsQuery,
                variables = new {
                    code = reportCode,
                    sourceID = playerId,
                    startTime = fightDetails.StartTime,
                    endTime = fightDetails.EndTime
                }
            };

            var eventsResponse = await httpClient.PostAsync($"{FFLogsApiBaseUrl}/client",
                new StringContent(JsonSerializer.Serialize(eventsRequest), Encoding.UTF8, "application/json"));

            if (!eventsResponse.IsSuccessStatusCode)
            {
                log.Error($"Failed to fetch events for player {playerId}: {eventsResponse.StatusCode}");
                return null;
            }

            var eventsJson = await eventsResponse.Content.ReadAsStringAsync();
            var eventsData = JsonSerializer.Deserialize<FFLogsV2EventsResponse>(eventsJson, JsonOptions.CaseInsensitive);

            var playerCastData = new PlayerCastData
            {
                ReportCode = reportCode,
                FightInfo = fightInfo,
                PlayerInfo = playerInfo,
                CastLogs = new List<CastLogEntry>()
            };

            var abilityMap = eventsData?.Data?.ReportData?.Report?.MasterData?.Abilities
                ?.ToDictionary(a => a.GameID, a => a);

            // pendingTimedCasts tracks begincast events waiting for their cast completion.
            // Key: (sourceID, abilityGameID), Value: queue of begincast timestamps.
            var pendingTimedCasts = new Dictionary<(long sourceId, int abilityGameId), Queue<long>>();

            if (eventsData?.Data?.ReportData?.Report?.Events?.Data is { } eventData)
            {
                foreach (var evt in eventData)
                {
                    var castEntry = ParseCastEventV2(evt, abilityMap, fightDetails.StartTime, pendingTimedCasts, playerInfo.JobId);
                    if (castEntry != null)
                        playerCastData.CastLogs.Add(castEntry);
                }
            }

            // The very first cast in a log is often a pre-pull cast whose begincast occurred
            // before FFLogs started recording. It arrives as an instant "cast" completion at
            // t≈0. Restore the cast duration and flag it as a precast.
            //
            // To account for spell speed, derive a speed ratio from the other casts in the log:
            // each cast with a recorded duration and a known Action-sheet base gives an
            // actual/base sample. The average of those samples is applied to the first event's
            // base cast time so the inferred duration matches the player's actual spell speed.
            if (playerCastData.CastLogs.Count > 0)
            {
                var first = playerCastData.CastLogs[0];
                if (first.IsInstant && first.Timestamp < 2000 && first.AbilityId > 0)
                {
                    var baseCastTimeSec = LookupActionCastTimeSec(first.AbilityId);
                    if (baseCastTimeSec > 0)
                    {
                        // Derive a speed ratio from actual/base cast-time pairs in the rest of
                        // the log (base >= 0.5 s to avoid distortion from near-instant abilities).
                        double ratioSum = 0;
                        int ratioCount = 0;
                        for (int i = 1; i < playerCastData.CastLogs.Count; i++)
                        {
                            var e = playerCastData.CastLogs[i];
                            if (e.IsInstant || e.CastTime <= 0 || e.AbilityId == 0) continue;
                            var b = LookupActionCastTimeSec(e.AbilityId);
                            if (b < 0.5) continue;
                            ratioSum += e.CastTime / b;
                            ratioCount++;
                        }

                        var speedRatio = ratioCount > 0 ? ratioSum / ratioCount : 1.0;
                        var adjustedCastTime = baseCastTimeSec * speedRatio;

                        first.IsInstant = false;
                        first.CastTime = adjustedCastTime;
                        first.IsPrecast = true;
                        log.Debug($"Pre-pull cast inferred for '{first.AbilityName}': base={baseCastTimeSec:F2}s ratio={speedRatio:F3} adjusted={adjustedCastTime:F2}s");
                    }
                }
            }

            log.Information($"Imported {playerCastData.CastLogs.Count} cast events for {playerInfo.Name}");

            return playerCastData;
        }
        catch (Exception ex)
        {
            log.Error($"Error fetching player cast events: {ex.Message}");
            return null;
        }
    }

    // FFLogs emits two events per timed cast:
    //   "begincast" (with a "duration" field) — fired when the cast bar starts
    //   "cast"                                — fired when the cast completes
    //
    // We emit the CastLogEntry on "begincast" (so the timestamp reflects when the player
    // pressed the button, not when the server confirmed the hit) and silently skip the
    // matching "cast" event. Unmatched "cast" events have no begincast and are instant casts.
    //
    // pendingTimedCasts tracks in-flight begincast events by (sourceID, abilityGameID).
    // A Queue is used per key because the same ability can be queued multiple times if
    // cast cancellations leave orphaned begincast entries.
    private CastLogEntry? ParseCastEventV2(
        JsonElement evt,
        Dictionary<int, FFLogsV2Ability>? abilityMap,
        long fightStartTime,
        Dictionary<(long sourceId, int abilityGameId), Queue<long>> pendingTimedCasts,
        uint jobId)
    {
        try
        {
            var type = evt.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            if (type != "cast" && type != "begincast")
                return null;

            if (!evt.TryGetProperty("timestamp", out var tsProp) || !tsProp.TryGetInt64(out long rawTimestamp))
                return null;

            if (!evt.TryGetProperty("sourceID", out var sidProp) || !sidProp.TryGetInt64(out long sourceId))
                return null;

            if (!evt.TryGetProperty("abilityGameID", out var abProp) || !abProp.TryGetInt32(out int abilityGameId))
                return null;

            var key = (sourceId, abilityGameId);

            if (type == "begincast" && evt.TryGetProperty("duration", out var durProp))
            {
                if (!pendingTimedCasts.TryGetValue(key, out var queue))
                {
                    queue = new Queue<long>();
                    pendingTimedCasts[key] = queue;
                }
                queue.Enqueue(rawTimestamp);

                var durationMs = durProp.TryGetDouble(out double dur) ? dur : 0;
                return BuildEntry(evt, abilityMap, abilityGameId, rawTimestamp, fightStartTime, durationMs / 1000.0, isInstant: false, jobId);
            }

            if (type == "cast")
            {
                if (pendingTimedCasts.TryGetValue(key, out var pending) && pending.Count > 0)
                {
                    // Already emitted on begincast — discard the completion event
                    pending.Dequeue();
                    return null;
                }

                // No matching begincast means this was an instant cast
                return BuildEntry(evt, abilityMap, abilityGameId, rawTimestamp, fightStartTime, castTime: 0, isInstant: true, jobId);
            }

            return null;
        }
        catch (Exception ex)
        {
            log.Warning($"Failed to parse cast event: {ex.Message}");
            return null;
        }
    }

    private CastLogEntry BuildEntry(
        JsonElement evt,
        Dictionary<int, FFLogsV2Ability>? abilityMap,
        int abilityGameId,
        long rawTimestamp,
        long fightStartTime,
        double castTime,
        bool isInstant,
        uint jobId)
    {
        string abilityName = "Unknown";
        string abilityType = string.Empty;
        if (abilityMap != null && abilityMap.TryGetValue(abilityGameId, out var ability))
        {
            abilityName = ability.Name ?? "Unknown";
            abilityType = ability.Type ?? string.Empty;
        }

        var sourceName = evt.TryGetProperty("source", out var srcProp) ? srcProp.GetString() ?? "Unknown" : "Unknown";

        return new CastLogEntry
        {
            Timestamp = (uint)Math.Max(0, rawTimestamp - fightStartTime),
            AbilityName = abilityName,
            AbilityId = (uint)abilityGameId,
            AbilityType = abilityType,
            SourceName = sourceName,
            SourceJobId = jobId,
            CastTime = castTime,
            IsInstant = isInstant,
            IsGcd = abilityType != "1",
            CachedTrailColor = JobUtilities.GetJobTrailColor(jobId)
        };
    }

    // Returns the cast time in seconds for the given ability, or 0 if unavailable.
    // Cast100ms is stored in units of 100 ms (e.g. 25 → 2.5 s).
    // Results are cached so each ability ID hits the Lumina sheet at most once.
    private static double LookupActionCastTimeSec(uint abilityId)
    {
        if (ActionCastTimeCache.TryGetValue(abilityId, out var cached))
            return cached;

        double result = 0;
        try
        {
#pragma warning disable PendingExcelSchema
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.Action>();
            if (sheet != null && sheet.HasRow(abilityId))
                result = sheet.GetRow(abilityId).Cast100ms / 10.0;
#pragma warning restore PendingExcelSchema
        }
        catch { }

        ActionCastTimeCache[abilityId] = result;
        return result;
    }

    public void Dispose()
    {
        httpClient?.Dispose();
    }
}
