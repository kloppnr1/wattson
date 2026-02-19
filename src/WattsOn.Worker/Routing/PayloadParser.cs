using System.Text.Json;

namespace WattsOn.Worker.Routing;

internal static class PayloadParser
{
    public static JsonElement Parse(string? rawPayload)
    {
        if (string.IsNullOrEmpty(rawPayload)) return default;
        return JsonSerializer.Deserialize<JsonElement>(rawPayload);
    }

    public static string? GetString(JsonElement payload, string property)
    {
        if (payload.ValueKind == JsonValueKind.Undefined || payload.ValueKind == JsonValueKind.Null)
            return null;
        return payload.TryGetProperty(property, out var value) ? value.GetString() : null;
    }
}
