using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Steamworks;

namespace NOSMR;

/// <summary>
/// Publishes modlist data to A2S_RULES via SteamGameServer.SetKeyValue().
/// Protocol v2: each mod is sent as its own rule key (nomm_d0..dN).
/// Known mods: 3-byte SHA-256 hash prefix of "id|version".
/// Unknown mods (null version): id + "UNK" suffix.
/// Keys: nomm_v (version), nomm_c (mod count), nomm_d0..dN (per-mod data),
///       nomm_h0..hN (data hash), nomm_r0..rN (required mods).
/// </summary>
public class A2SRulesPublisher
{
    private const double RepublishIntervalSeconds = 30.0;

    private readonly ConcurrentQueue<(string key, string? value)> _queue = new();
    private readonly FileLogger? _logger;
    private bool _loggedOn;
    private double _republishCountdown;
    private bool _hasRepublishedOnce;

    private List<PackageReference>? _lastModlist;
    private string? _lastDataHash;
    private string? _lastRequired;
    private int _lastModCount;
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
        var requiredIds = requiredMods != null && requiredMods.Count > 0
            ? string.Join(";", requiredMods)
            : null;

        var dataJson = PackageReference.SerializeList(modlist);
        var dataHash = NommProtocol.ComputeDataHash(dataJson);

        _logger?.Info($"Publishing modlist: {modlist.Count} mod(s), dataHash={dataHash.Substring(0, Math.Min(12, dataHash.Length))}...");

        _lastModlist = modlist;
        _lastDataHash = dataHash;
        _lastRequired = requiredIds;

        EnqueuePerMod(modlist, dataHash, requiredIds);
    }

    public void Clear()
    {
        if (_lastModlist == null) return;

        _queue.Enqueue((NommProtocol.KeyVersion, ""));
        _queue.Enqueue((NommProtocol.KeyChunkCount, ""));

        for (int i = 0; i < _lastModCount; i++)
            _queue.Enqueue(($"{NommProtocol.KeyDataPrefix}{i}", ""));

        for (int i = 0; i < _lastHashChunkCount; i++)
            _queue.Enqueue(($"{NommProtocol.KeyHashPrefix}{i}", ""));

        for (int i = 0; i < _lastRequiredChunkCount; i++)
            _queue.Enqueue(($"{NommProtocol.KeyRequiredPrefix}{i}", ""));

        _lastModlist = null;
        _lastDataHash = null;
        _lastRequired = null;
        _lastModCount = 0;
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

        if (_lastModlist != null)
        {
            _republishCountdown -= deltaTime;
            if (_republishCountdown <= 0)
            {
                _republishCountdown = RepublishIntervalSeconds;
                if (_loggedOn)
                {
                    EnqueuePerMod(_lastModlist, _lastDataHash!, _lastRequired);
                    if (!_hasRepublishedOnce)
                    {
                        _hasRepublishedOnce = true;
                        _logger?.Info("First periodic republish triggered");
                    }
                }
            }
        }
    }

    private void EnqueuePerMod(List<PackageReference> modlist, string dataHash, string? required)
    {
        var hashChunks = NommProtocol.SplitIntoChunks(dataHash);
        var requiredChunks = required != null ? NommProtocol.SplitIntoChunks(required) : new List<string>();

        _lastModCount = modlist.Count;
        _lastHashChunkCount = hashChunks.Count;
        _lastRequiredChunkCount = requiredChunks.Count;

        _queue.Enqueue((NommProtocol.KeyVersion, NommProtocol.ProtocolVersion));
        _queue.Enqueue((NommProtocol.KeyChunkCount, modlist.Count.ToString()));

        for (int i = 0; i < modlist.Count; i++)
        {
            var mod = modlist[i];
            string value;
            if (mod.Version != null)
            {
                value = NommProtocol.ComputeModHashPrefix(mod.Id, mod.Version.ToString());
            }
            else
            {
                value = mod.Id + "UNK";
            }
            _queue.Enqueue(($"{NommProtocol.KeyDataPrefix}{i}", value));
        }

        for (int i = 0; i < hashChunks.Count; i++)
            _queue.Enqueue(($"{NommProtocol.KeyHashPrefix}{i}", hashChunks[i]));

        for (int i = 0; i < requiredChunks.Count; i++)
            _queue.Enqueue(($"{NommProtocol.KeyRequiredPrefix}{i}", requiredChunks[i]));
    }
}
