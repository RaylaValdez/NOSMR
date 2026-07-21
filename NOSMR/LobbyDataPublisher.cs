using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Steamworks;

namespace NOSMR;

/// <summary>
/// Publishes modlist data to Steam Lobby metadata via SteamMatchmaking.SetLobbyData().
/// Uses the same nomm_* key scheme as A2SRulesPublisher for client-hosted sessions.
/// </summary>
public class LobbyDataPublisher
{
    private const double RepublishIntervalSeconds = 30.0;
    private const double LobbySearchIntervalSeconds = 5.0;
    private const int MaxPerFrame = 3;
    private const int MaxSearchAttempts = 3;

    private readonly ConcurrentQueue<(string key, string? value)> _queue = new();
    private readonly FileLogger? _logger;

    private CSteamID _lobbyId;
    private bool _lobbyReady;
    private double _lobbySearchCountdown;
    private double _republishCountdown;
    private bool _hasRepublishedOnce;

    private List<PackageReference>? _lastModlist;
    private string? _lastDataHash;
    private string? _lastRequired;
    private int _lastModCount;
    private int _lastHashChunkCount;
    private int _lastRequiredChunkCount;

    private readonly HashSet<string> _activeKeys = new();

    private CallResult<LobbyMatchList_t>? _lobbyListResult;
    private Callback<LobbyCreated_t>? _lobbyCreatedCb;
    private Callback<LobbyEnter_t>? _lobbyEnterCb;
    private bool _searchInFlight;
    private int _searchAttempts;

    public LobbyDataPublisher(FileLogger? logger)
    {
        _logger = logger;
        _lobbySearchCountdown = 3.0;
    }

    public void RegisterCallbacks()
    {
        _lobbyListResult = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
        _lobbyCreatedCb = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        _lobbyEnterCb = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
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

        _lastModlist = modlist;
        _lastDataHash = dataHash;
        _lastRequired = requiredIds;
        _searchAttempts = 0;

        EnqueuePerMod(modlist, dataHash, requiredIds);
    }

    public void Clear()
    {
        if (_lastModlist == null || !_lobbyReady) return;

        foreach (var key in _activeKeys)
            _queue.Enqueue((key, null));

        _lastModlist = null;
        _lastDataHash = null;
        _lastRequired = null;
        _lastModCount = 0;
        _lastHashChunkCount = 0;
        _lastRequiredChunkCount = 0;
        _activeKeys.Clear();
    }

    public void ProcessPendingUpdates(double deltaTime)
    {
        if (!_lobbyReady)
        {
            if (_searchAttempts >= MaxSearchAttempts) return;

            _lobbySearchCountdown -= deltaTime;
            if (_lobbySearchCountdown <= 0 && !_searchInFlight)
            {
                FindMyLobby();
                _lobbySearchCountdown = LobbySearchIntervalSeconds;
            }
            return;
        }

        int processed = 0;
        while (processed < MaxPerFrame && _queue.TryDequeue(out var item))
        {
            try
            {
                if (item.value != null)
                {
                    SteamMatchmaking.SetLobbyData(_lobbyId, item.key, item.value);
                    _activeKeys.Add(item.key);
                }
                else
                {
                    SteamMatchmaking.DeleteLobbyData(_lobbyId, item.key);
                    _activeKeys.Remove(item.key);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to set lobby key '{item.key}'", ex);
            }
            processed++;
        }

        if (_lastModlist != null)
        {
            _republishCountdown -= deltaTime;
            if (_republishCountdown <= 0)
            {
                _republishCountdown = RepublishIntervalSeconds;
                EnqueuePerMod(_lastModlist, _lastDataHash!, _lastRequired);
                if (!_hasRepublishedOnce)
                {
                    _hasRepublishedOnce = true;
                    _logger?.Info("First periodic lobby republish triggered");
                }
            }
        }
    }

    private void FindMyLobby()
    {
        CSteamID myId;
        try { myId = SteamUser.GetSteamID(); }
        catch { return; }

        try
        {
            _searchInFlight = true;
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(
                ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            var handle = SteamMatchmaking.RequestLobbyList();
            _lobbyListResult?.Set(handle);
        }
        catch (Exception ex)
        {
            _searchInFlight = false;
            _logger?.Error("Failed to request lobby list", ex);
        }
    }

    private void OnLobbyMatchList(LobbyMatchList_t result, bool bIOFailure)
    {
        _searchInFlight = false;
        if (result.m_nLobbiesMatching == 0)
        {
            _searchAttempts++;
            return;
        }

        CSteamID myId;
        try { myId = SteamUser.GetSteamID(); }
        catch { return; }

        for (uint i = 0; i < result.m_nLobbiesMatching; i++)
        {
            CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex((int)i);
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobbyId);

            if (owner.m_SteamID == myId.m_SteamID)
            {
                _lobbyId = lobbyId;
                _lobbyReady = true;
                _logger?.Info($"Found my lobby: {lobbyId}");

                if (_lastModlist != null)
                    EnqueuePerMod(_lastModlist, _lastDataHash!, _lastRequired);
                return;
            }
        }

        if (!_lobbyReady)
            _searchAttempts++;
    }

    private void OnLobbyCreated(LobbyCreated_t result)
    {
        if (result.m_eResult == EResult.k_EResultOK)
        {
            _lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            _lobbyReady = true;
            _searchAttempts = 0;
            _logger?.Info($"Lobby created: {_lobbyId}");

            if (_lastModlist != null)
                EnqueuePerMod(_lastModlist, _lastDataHash!, _lastRequired);
        }
    }

    private void OnLobbyEnter(LobbyEnter_t result)
    {
        if (result.m_EChatRoomEnterResponse != 1) return;

        var lobbyId = new CSteamID(result.m_ulSteamIDLobby);

        try
        {
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
            CSteamID myId = SteamUser.GetSteamID();

            if (owner.m_SteamID == myId.m_SteamID)
            {
                _lobbyId = lobbyId;
                _lobbyReady = true;
                _searchAttempts = 0;
                _logger?.Info($"Entered own lobby: {lobbyId}");

                if (_lastModlist != null)
                    EnqueuePerMod(_lastModlist, _lastDataHash!, _lastRequired);
            }
        }
        catch
        {
            // Steam not ready yet, will be found by periodic search
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
