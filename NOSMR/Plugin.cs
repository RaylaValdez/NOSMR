using System;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

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
    private A2SRulesPublisher _publisher = null!;
    private FileLogger? _debugLog;

    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;

    private string PluginDir => Path.GetDirectoryName(Info.Location)!;
    private string ModpacksDir => Path.Combine(PluginDir, "modpacks");

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
            _publisher = new A2SRulesPublisher(_debugLog);

            Log.LogInfo("[NOSMR] Loaded");

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
        catch (Exception ex)
        {
            Log.LogError($"[NOSMR] Failed to initialize: {ex}");
            _debugLog?.Error("Failed to initialize", ex);
        }
    }

    private void Update()
    {
        _publisher.ProcessPendingUpdates(Time.deltaTime);
    }

    private void OnDestroy()
    {
        _publisher.Clear();

        // Drain remaining queued updates so Clear() takes effect
        for (var i = 0; i < 10; i++)
        {
            _publisher.ProcessPendingUpdates(0);
            Thread.Sleep(10);
        }

        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        _debugLog?.Dispose();

        Log.LogInfo("[NOSMR] Unloaded");
    }

    /// <summary>
    /// Read .nommpack files and publish the modlist to A2S_RULES.
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

            _publisher.Publish(modlist, _config.RequiredModSet);
            Log.LogInfo($"[NOSMR] Published {modlist.Count} mod(s) to A2S_RULES");
        }
        catch (Exception ex)
        {
            Log.LogError($"[NOSMR] Refresh failed: {ex}");
            _debugLog?.Error("Refresh failed", ex);
        }
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
