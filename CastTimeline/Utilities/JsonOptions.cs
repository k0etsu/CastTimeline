using System.Text.Json;

namespace CastTimeline.Utilities;

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
}
