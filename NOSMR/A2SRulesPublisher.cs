using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Steamworks;

namespace NOSMR;

/// <summary>
/// Publishes modlist data to A2S_RULES via SteamGameServer.SetKeyValue().
/// Data is split into 64-byte chunks to stay under the per-rule value limit
/// observed in Nuclear Option's own rules (d0-d3 are each ~64 bytes).
/// Keys: nomm_v (version), nomm_c (chunk count), nomm_d0..dN (data chunks),
///       nomm_h0..hN (hash chunks), nomm_r0..rN (required mod chunks).
/// </summary>
public class A2SRulesPublisher
{
    private const int ChunkSize = 64;

    private const string KeyVersion = "nomm_v";
    private const string KeyChunkCount = "nomm_c";
    private const string KeyDataPrefix = "nomm_d";
    private const string KeyHashPrefix = "nomm_h";
    private const string KeyRequiredPrefix = "nomm_r";

    private const string ProtocolVersion = "1";
    private const double RepublishIntervalSeconds = 30.0;

    private readonly ConcurrentQueue<(string key, string? value)> _queue = new();
    private readonly FileLogger? _logger;
    private bool _loggedOn;
    private double _republishCountdown;
    private bool _hasRepublishedOnce;

    private string? _lastDataJson;
    private string? _lastHash;
    private string? _lastRequired;
    private int _lastChunkCount;
    private int _lastHashChunkCount;
    private int _lastRequiredChunkCount;

    public A2SRulesPublisher(FileLogger? logger)
    {
        _logger = logger;
    }

    public void Publish(
        List<PackageReference> modlist,
        HashSet<string>? requiredMods = null)
    {
        var dataJson = PackageReference.SerializeList(modlist);
        var hash = ComputeHash(dataJson);

        var requiredIds = requiredMods != null && requiredMods.Count > 0
            ? string.Join(";", requiredMods)
            : null;

        _logger?.Info($"Publishing modlist: {modlist.Count} mod(s), hash={hash.Substring(0, Math.Min(12, hash.Length))}...");

        _lastDataJson = dataJson;
        _lastHash = hash;
        _lastRequired = requiredIds;

        EnqueueChunked(dataJson, hash, requiredIds);
    }

    public void Clear()
    {
        if (_lastDataJson == null) return;

        _queue.Enqueue((KeyVersion, ""));
        _queue.Enqueue((KeyChunkCount, ""));

        for (int i = 0; i < _lastChunkCount; i++)
            _queue.Enqueue(($"{KeyDataPrefix}{i}", ""));

        for (int i = 0; i < _lastHashChunkCount; i++)
            _queue.Enqueue(($"{KeyHashPrefix}{i}", ""));

        for (int i = 0; i < _lastRequiredChunkCount; i++)
            _queue.Enqueue(($"{KeyRequiredPrefix}{i}", ""));

        _lastDataJson = null;
        _lastHash = null;
        _lastRequired = null;
        _lastChunkCount = 0;
        _lastHashChunkCount = 0;
        _lastRequiredChunkCount = 0;
        _logger?.Info("Queued NOMM key clear");
    }

    public void ProcessPendingUpdates(double deltaTime)
    {
        try
        {
            var isLoggedOn = SteamGameServer.BLoggedOn();
            if (isLoggedOn && !_loggedOn)
            {
                _logger?.Info("SteamGameServer.BLoggedOn() is now true");
                _loggedOn = true;
            }
            else if (!isLoggedOn && _loggedOn)
            {
                _logger?.Warn("SteamGameServer.BLoggedOn() returned false - game may be shutting down");
                _loggedOn = false;
            }
        }
        catch
        {
        }

        if (_loggedOn)
        {
            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    if (item.value != null)
                        SteamGameServer.SetKeyValue(item.key, item.value);
                    else
                        SteamGameServer.ClearAllKeyValues();
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Failed to set A2S_RULES key '{item.key}'", ex);
                }
            }
        }

        if (_lastDataJson != null)
        {
            _republishCountdown -= deltaTime;
            if (_republishCountdown <= 0)
            {
                _republishCountdown = RepublishIntervalSeconds;
                if (_loggedOn)
                {
                    EnqueueChunked(_lastDataJson, _lastHash!, _lastRequired);
                    if (!_hasRepublishedOnce)
                    {
                        _hasRepublishedOnce = true;
                        _logger?.Info("First periodic republish triggered");
                    }
                }
            }
        }
    }

    private void EnqueueChunked(string dataJson, string hash, string? required)
    {
        var dataChunks = SplitIntoChunks(dataJson);
        var hashChunks = SplitIntoChunks(hash);
        var requiredChunks = required != null ? SplitIntoChunks(required) : new List<string>();

        _lastChunkCount = dataChunks.Count;
        _lastHashChunkCount = hashChunks.Count;
        _lastRequiredChunkCount = requiredChunks.Count;

        _queue.Enqueue((KeyVersion, ProtocolVersion));
        _queue.Enqueue((KeyChunkCount, dataChunks.Count.ToString()));

        for (int i = 0; i < dataChunks.Count; i++)
            _queue.Enqueue(($"{KeyDataPrefix}{i}", dataChunks[i]));

        for (int i = 0; i < hashChunks.Count; i++)
            _queue.Enqueue(($"{KeyHashPrefix}{i}", hashChunks[i]));

        for (int i = 0; i < requiredChunks.Count; i++)
            _queue.Enqueue(($"{KeyRequiredPrefix}{i}", requiredChunks[i]));
    }

    private static List<string> SplitIntoChunks(string value)
    {
        var chunks = new List<string>();
        for (int i = 0; i < value.Length; i += ChunkSize)
        {
            int length = Math.Min(ChunkSize, value.Length - i);
            chunks.Add(value.Substring(i, length));
        }
        return chunks;
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
