using System.Text.Json;

namespace Stroll.Runner.HistoryIntegrity;

public static class JsonUtil
{
    public static JsonElement Require(this JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) throw new Exception($"missing property '{prop}'");
        return v;
    }

    public static string GetStringOrEmpty(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    public static double GetDouble(this JsonElement e, params string[] keys)
    {
        foreach (var k in keys)
            if (e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.Number) return v.GetDouble();
        return 0d;
    }
}
