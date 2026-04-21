using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CastTimeline.Services;

// Response wrapper tree: FFLogsV2Response → Data → ReportData → Report
// Used by both GetReportFightsAsync and GetPlayerCastEventsAsync.
public class FFLogsV2Response
{
    public FFLogsV2Data? Data { get; set; }
}

public class FFLogsV2Data
{
    public FFLogsV2ReportData? ReportData { get; set; }
}

public class FFLogsV2ReportData
{
    public FFLogsV2Report? Report { get; set; }
}

public class FFLogsV2Report
{
    public long StartTime { get; set; }
    public FFLogsV2Zone? Zone { get; set; }
    public List<FFLogsV2Fight>? Fights { get; set; }
    public FFLogsV2Events? Events { get; set; }
    public FFLogsV2MasterData? MasterData { get; set; }
}

public class FFLogsV2MasterData
{
    public List<FFLogsV2Ability>? Abilities { get; set; }
    // Players are actors of type "Player" — aliased in the GraphQL query
    public List<FFLogsV2Player>? Players { get; set; }
}

public class FFLogsV2Ability
{
    public int GameID { get; set; }
    public string? Name { get; set; }
    // Type "1" = oGCD ability/item; see CastLogEntry.AbilityType
    public string? Type { get; set; }
}

public class FFLogsV2Player
{
    public int GameID { get; set; }
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Server { get; set; }
    public object? Type { get; set; }
    public object? SubType { get; set; }
}

public class FFLogsV2Zone
{
    public string? Name { get; set; }
}

public class FFLogsV2Fight
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public long StartTime { get; set; }
    public long EndTime { get; set; }
    public int EncounterID { get; set; }
    public List<int>? FriendlyPlayers { get; set; }
    public bool? Kill { get; set; }
}

public class FFLogsV2Events
{
    public List<System.Text.Json.JsonElement>? Data { get; set; }
    // NextPageTimestamp would be used to paginate events beyond 10,000 — not yet implemented
}

// Separate response type for GetPlayerCastEventsAsync — structurally identical to FFLogsV2Response
// but kept distinct to make the two call sites explicit.
public class FFLogsV2EventsResponse
{
    public FFLogsV2Data? Data { get; set; }
}

public class OAuth2TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
