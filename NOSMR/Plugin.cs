using System;
using System.Collections;
using System.IO;
using System.Threading;
using GameSocketType = NuclearOption.Networking.SocketType;
using BepInEx;
using BepInEx.Logging;
using Mirage;
using Steamworks;
using UnityEngine;
using NuclearOption.Networking;
using NuclearOption;

namespace NOSMR;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = PluginInfo.PLUGIN_GUID;
    public const string Name = PluginInfo.PLUGIN_NAME;
    public const string Version = PluginInfo.PLUGIN_VERSION;

    internal static Plugin Instance = null!;
    internal static ManualLogSource Log = null!;

    private NOSMRConfig _config = null!;
    private ModpackLoader _modpackLoader = null!;
    private A2SRulesPublisher? _a2sPublisher;
    private LobbyDataPublisher? _lobbyPublisher;
    private FileLogger? _debugLog;

    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;

    private ConnectRequest? _pendingRequest;

    private string PluginDir => Path.GetDirectoryName(Info.Location)!;
    private string ModpacksDir => Path.Combine(PluginDir, "modpacks");
    private static string ConfigDir => Path.Combine(
        Path.GetDirectoryName(Instance.Info.Location)!,
        "..", "..", "config");

    private void Awake()
    {
        Instance = this;
        Log = base.Logger;

        try
        {
            _config = new NOSMRConfig(Config);

            if (_config.EnableDebugLog.Value)
                _debugLog = new FileLogger(PluginDir);

            _modpackLoader = new ModpackLoader(_debugLog);

            Log.LogInfo("[NOSMR] Loaded");

            _pendingRequest = ConnectRequest.ReadFromFile(ConfigDir);
            if (_pendingRequest != null)
            {
                Log.LogInfo($"[NOSMR] Connect request: {_pendingRequest.Host}:{_pendingRequest.Port} steamId={_pendingRequest.SteamId}");
                StartCoroutine(WaitForMenuReady());
            }

            StartCoroutine(DetectAndInitialize());
        }
        catch (Exception ex)
        {
            Log.LogError($"[NOSMR] Failed to initialize: {ex}");
            _debugLog?.Error("Failed to initialize", ex);
        }
    }

    private IEnumerator DetectAndInitialize()
    {
        // Wait for Steam to be ready
        while (!IsSteamReady())
        {
            yield return null;
        }

        if (IsDedicatedServer())
        {
            _a2sPublisher = new A2SRulesPublisher(_debugLog);
            Log.LogInfo("[NOSMR] Detected dedicated server - using A2S_RULES");
        }
        else
        {
            _lobbyPublisher = new LobbyDataPublisher(_debugLog);
            _lobbyPublisher.RegisterCallbacks();
            Log.LogInfo("[NOSMR] Detected client-hosted - using lobby metadata");
        }

        if (_config.Enabled.Value)
        {
            StartWatching();
            Refresh();
        }
        else
        {
            Log.LogInfo("[NOSMR] Broadcasting disabled via config");
        }
    }

    private bool IsSteamReady()
    {
        try
        {
            SteamGameServer.BLoggedOn();
            return true;
        }
        catch
        {
            try
            {
                SteamUser.GetSteamID();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private void Update()
    {
        _a2sPublisher?.ProcessPendingUpdates(Time.deltaTime);
        _lobbyPublisher?.ProcessPendingUpdates(Time.deltaTime);
    }

    private void OnDestroy()
    {
        _a2sPublisher?.Clear();
        _lobbyPublisher?.Clear();

        for (var i = 0; i < 10; i++)
        {
            _a2sPublisher?.ProcessPendingUpdates(0);
            _lobbyPublisher?.ProcessPendingUpdates(0);
            Thread.Sleep(10);
        }

        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        _debugLog?.Dispose();

        Log.LogInfo("[NOSMR] Unloaded");
    }

    /// <summary>
    /// Read .nommpack files and publish the modlist to the appropriate transport.
    /// </summary>
    internal void Refresh()
    {
        if (!_config.Enabled.Value) return;

        try
        {
            var modpacksDir = ModpacksDir;
            var modlist = _modpackLoader.Load(modpacksDir);

            if (modlist.Count == 0)
            {
                Log.LogWarning("[NOSMR] No mods found in .nommpack files - not broadcasting");
                _debugLog?.Warn("No mods found in .nommpack files");
                return;
            }

            _a2sPublisher?.Publish(modlist, _config.RequiredModSet);
            _lobbyPublisher?.Publish(modlist, _config.RequiredModSet);

            var target = _a2sPublisher != null ? "A2S_RULES" : "lobby metadata";
            Log.LogInfo($"[NOSMR] Published {modlist.Count} mod(s) to {target}");
        }
        catch (Exception ex)
        {
            Log.LogError($"[NOSMR] Refresh failed: {ex}");
            _debugLog?.Error("Refresh failed", ex);
        }
    }

    private IEnumerator WaitForMenuReady()
    {
        while (MainMenu.State != MainMenu.LoadingState.Loaded)
        {
            if (MainMenu.ApplicationIsQuitting) yield break;
            yield return null;
        }

        while (NetworkManagerNuclearOption.i == null)
        {
            if (MainMenu.ApplicationIsQuitting) yield break;
            yield return null;
        }

        while (NetworkManagerNuclearOption.i.Client == null)
        {
            if (MainMenu.ApplicationIsQuitting) yield break;
            yield return null;
        }

        DoConnect(_pendingRequest!);
    }

    private void DoConnect(ConnectRequest request)
    {
        try
        {
            GameSocketType socketType;
            ConnectOptions options;

            if (request.SteamId > 0)
            {
                socketType = GameSocketType.Steam;
                options = new ConnectOptions(socketType, request.SteamId.ToString());
            }
            else if (IsSteamId(request.Host))
            {
                socketType = GameSocketType.Steam;
                options = new ConnectOptions(socketType, request.Host);
            }
            else
            {
                socketType = GameSocketType.UDP;
                options = new ConnectOptions(socketType, request.Host, request.Port);
            }

            if (!string.IsNullOrEmpty(request.Password))
            {
                options.Password = request.Password;
            }

            Log.LogInfo($"[NOSMR] Connecting via {socketType}...");
            NetworkManagerNuclearOption.i.Client.Disconnected.RemoveListener(OnDisconnected);
            NetworkManagerNuclearOption.i.Client.Disconnected.AddListener(OnDisconnected);
            NetworkManagerNuclearOption.i.StartClient(options);
        }
        catch (Exception ex)
        {
            Log.LogError($"[NOSMR] Failed to connect: {ex}");
        }
    }

    private void OnDisconnected(ClientStoppedReason reason)
    {
        Log.LogInfo($"[NOSMR] Disconnected: {reason}");
    }

    private static bool IsDedicatedServer()
    {
        try
        {
            SteamGameServer.BLoggedOn();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSteamId(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (!ulong.TryParse(value, out var id)) return false;
        return id > 1_000_000_000_000;
    }

    private void StartWatching()
    {
        var modpacksDir = ModpacksDir;
        Directory.CreateDirectory(modpacksDir);

        _watcher = new FileSystemWatcher(modpacksDir)
        {
            Filter = "*.nommpack",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            IncludeSubdirectories = false,
        };

        _watcher.Changed += OnModpackChanged;
        _watcher.Created += OnModpackChanged;
        _watcher.Deleted += OnModpackChanged;
        _watcher.Renamed += OnModpackChanged;
        _watcher.Error += OnWatcherError;

        _debounceTimer = new System.Timers.Timer(300);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) => Refresh();

        _watcher.EnableRaisingEvents = true;
        _debugLog?.Info($"Watching {modpacksDir} for .nommpack changes");
    }

    private void OnModpackChanged(object sender, FileSystemEventArgs e)
    {
        _debugLog?.Debug($"File event: {e.ChangeType} {e.Name}");
        ResetDebounce();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _debugLog?.Error($"Watcher error: {e.GetException().Message}");
        ResetDebounce();
    }

    private void ResetDebounce()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }
}
