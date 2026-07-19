using System;
using System.IO;

namespace NOSMR;

public class ConnectRequest
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string? Password { get; set; }
    public ulong SteamId { get; set; }

    private const string FileName = "nomm-connect.json";

    public static string GetFilePath(string configDir)
    {
        return Path.Combine(configDir, FileName);
    }

    public static ConnectRequest? ReadFromFile(string configDir)
    {
        var path = GetFilePath(configDir);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var request = Deserialize(json);

            try { File.Delete(path); } catch { }

            return request;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[NOSMR] Failed to read connect request: {ex.Message}");
            return null;
        }
    }

    internal static ConnectRequest Deserialize(string json)
    {
        json = json.Trim();

        var host = ExtractString(json, "host");
        var port = ExtractInt(json, "port");
        var password = ExtractNullableString(json, "password");
        var steamId = ExtractULong(json, "steamId");

        if (string.IsNullOrEmpty(host))
            throw new FormatException("Missing or empty 'host' field");

        return new ConnectRequest
        {
            Host = host,
            Port = port,
            Password = password,
            SteamId = steamId,
        };
    }

    private static string ExtractString(string json, string key)
    {
        var search = $"\"{key}\"";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return "";

        idx = json.IndexOf(':', idx + search.Length);
        if (idx < 0) return "";

        idx++;
        while (idx < json.Length && json[idx] == ' ') idx++;

        if (idx >= json.Length || json[idx] != '"') return "";

        var start = idx + 1;
        var end = FindStringEnd(json, start);
        return UnescapeJsonString(json.Substring(start, end - start));
    }

    private static string? ExtractNullableString(string json, string key)
    {
        var search = $"\"{key}\"";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;

        idx = json.IndexOf(':', idx + search.Length);
        if (idx < 0) return null;

        idx++;
        while (idx < json.Length && json[idx] == ' ') idx++;

        if (idx >= json.Length) return null;
        if (idx + 4 <= json.Length && json.Substring(idx, 4) == "null") return null;
        if (json[idx] != '"') return null;

        var start = idx + 1;
        var end = FindStringEnd(json, start);
        return UnescapeJsonString(json.Substring(start, end - start));
    }

    private static int ExtractInt(string json, string key)
    {
        var search = $"\"{key}\"";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return 0;

        idx = json.IndexOf(':', idx + search.Length);
        if (idx < 0) return 0;

        idx++;
        while (idx < json.Length && json[idx] == ' ') idx++;

        var start = idx;
        while (idx < json.Length && char.IsDigit(json[idx])) idx++;

        return int.TryParse(json.Substring(start, idx - start), out var val) ? val : 0;
    }

    private static ulong ExtractULong(string json, string key)
    {
        var search = $"\"{key}\"";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return 0;

        idx = json.IndexOf(':', idx + search.Length);
        if (idx < 0) return 0;

        idx++;
        while (idx < json.Length && json[idx] == ' ') idx++;

        var start = idx;
        while (idx < json.Length && char.IsDigit(json[idx])) idx++;

        return ulong.TryParse(json.Substring(start, idx - start), out var val) ? val : 0;
    }

    private static int FindStringEnd(string json, int start)
    {
        var idx = start;
        while (idx < json.Length)
        {
            if (json[idx] == '\\')
            {
                idx += 2;
                continue;
            }
            if (json[idx] == '"') return idx;
            idx++;
        }
        return idx;
    }

    private static string UnescapeJsonString(string value)
    {
        return value
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t");
    }
}
