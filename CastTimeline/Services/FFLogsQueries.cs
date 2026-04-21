namespace CastTimeline.Services;

internal static class FFLogsQueries
{
    // Full report query including masterData with player roster.
    // Used by GetReportFightsAsync to populate the fight and player selection UI.
    internal const string ReportFightsQuery = @"
    query($code: String!) {
        reportData {
            report(code: $code) {
                startTime
                zone {
                    name
                }
                masterData {
                    abilities {
                        gameID
                        name
                        type
                    }
                    players: actors(type: ""Player"") {
                        gameID
                        id
                        name
                        server
                        type
                        subType
                    }
                }
                fights {
                    id
                    name
                    startTime
                    endTime
                    encounterID
                    friendlyPlayers
                    kill
                }
            }
        }
    }";

    // Fetch start/end times for a specific fight.
    // Called first in GetPlayerCastEventsAsync to bound the events query.
    internal const string FightTimesQuery = @"
    query($code: String!, $fightID: Int!) {
        reportData {
            report(code: $code) {
                fights(fightIDs: [$fightID]) {
                    startTime
                    endTime
                }
            }
        }
    }";

    // Cast events filtered to a specific player within a fight window.
    // Limit is 10,000 events — pagination via nextPageTimestamp is not yet implemented.
    internal const string PlayerEventsQuery = @"
    query($code: String!, $sourceID: Int!, $startTime: Float!, $endTime: Float!){
        reportData {
            report(code: $code) {
                events(sourceID: $sourceID, startTime: $startTime, endTime: $endTime, limit: 10000, dataType: Casts) {
                    data
                }
                masterData {
                    abilities {
                        gameID
                        name
                        type
                    }
                }
            }
        }
    }";
}
