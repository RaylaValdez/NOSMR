using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Steamworks;

namespace NOSMR;

/// <summary>
/// Publishes modlist data to A2S_RULES via SteamGameServer.SetKeyValue().
/// All SteamGameServer calls are marshalled to the main thread via a concurrent queue.
/// </summary>
public class A2SRulesPublisher
{
    private const string KeyVersion = "nomm_version";
    private const string KeyData = "nomm_data";
    private const string KeyHash = "nomm_hash";
    private const string KeyRequired = "nomm_required";

    private const string ProtocolVersion = "1";

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private readonly ConcurrentQueue<(string key, string? value)> _queue = new();
    private readonly FileLogger? _logger;
    private bool _published;

    public A2SRulesPublisher(FileLogger? logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Queue a modlist publish. Call from any thread; the actual Steamworks
    /// calls happen when <see cref="ProcessPendingUpdates"/> is invoked on the main thread.
    /// </summary>
    public void Publish(
        List<PackageReference> modlist,
        HashSet<string>? requiredMods = null)
    {
        var dataJson = JsonConvert.SerializeObject(modlist, Formatting.None, SerializerSettings);
        var hash = ComputeHash(dataJson);

        var requiredIds = requiredMods != null && requiredMods.Count > 0
            ? string.Join(";", requiredMods)
            : null;

        _logger?.Info($"Publishing modlist: {modlist.Count} mod(s), hash={hash.Substring(0, Math.Min(12, hash.Length))}...");

        _queue.Enqueue((KeyVersion, ProtocolVersion));
        _queue.Enqueue((KeyData, dataJson));
        _queue.Enqueue((KeyHash, hash));
        _queue.Enqueue((KeyRequired, requiredIds));

        _published = true;
    }

    /// <summary>
    /// Clear all NOMM keys from A2S_RULES by setting them to empty strings.
    /// Used on plugin unload.
    /// </summary>
    public void Clear()
    {
        if (!_published) return;

        _queue.Enqueue((KeyVersion, ""));
        _queue.Enqueue((KeyData, ""));
        _queue.Enqueue((KeyHash, ""));
        _queue.Enqueue((KeyRequired, ""));

        _published = false;
        _logger?.Info("Queued NOMM key clear");
    }

    /// <summary>
    /// Process all queued Steamworks calls on the main thread.
    /// Must be called from Plugin.Update().
    /// </summary>
    public void ProcessPendingUpdates()
    {
        while (_queue.TryDequeue(out var item))
        {
            try
            {
                if (item.value != null)
                    SteamGameServer.SetKeyValue(item.key, item.value);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to set A2S_RULES key '{item.key}'", ex);
            }
        }
    }

    private static string ComputeHash(string data)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
            var sb = new StringBuilder("sha256:");
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
