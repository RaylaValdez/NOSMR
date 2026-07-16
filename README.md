# NOSMR

Nuclear Option Server Mod Reporter. A server-side BepInEx plugin that tells
NOMM clients what mods your server is running, so they don't have to guess.

## What it does

When a NOMM client queries your server, it normally has no idea what mods are
installed. NOSMR fixes that by writing your modlist into the A2S_RULES response
that Steam already provides. Clients running NOMM will automatically see which
mods you have, which ones they're missing, and which versions don't match.

## Manual Installation

Drop `NOSMR.dll` into your server's `BepInEx/plugins/` folder. That's it.

NOSMR needs BepInEx 5.x and a working Steam Game Server connection. If the
server can be found in the Steam server browser, NOSMR will work.

## Setting up your modpack

NOSMR reads `.nommpack` files from a `modpacks/` folder next to the DLL.

### Directory layout

```
BepInEx/plugins/NOSMR.dll
BepInEx/plugins/NOSMR.modpacks/
  my-server-pack.nommpack
  another-pack.nommpack
```

If the `modpacks/` folder doesn't exist, NOSMR will create it on startup.

## Configuration

On first run, BepInEx generates a config file at
`BepInEx/config/gerryofravine.nosmr.cfg`:

```ini
[General]

## Enable NOMM modlist broadcasting via A2S_RULES.
## When disabled, NOMM keys are removed from the server query response.
Enabled = true

## Comma-separated list of mod IDs that are required to join this server.
## These are broadcast to NOMM clients for pre-join validation.
## Leave empty to mark all mods as optional.
## Example: aryx.f16m, no-autopilot-mod, SmokeTrail
RequiredMods = 

[Debug]

## Write detailed debug information to NOSMR/debug/debug.log.
## Useful for troubleshooting, but increases log file size.
EnableDebugLog = false
```

## How it works

NOSMR hooks into `SteamGameServer.SetKeyValue()` to write key-value pairs into
the server's A2S_RULES response. The modlist is split into 64-byte chunks to
stay under the per-rule value limit that Nuclear Option enforces. The game
itself does the same thing with its mission description (the `d0`-`d3` rules).

The keys use short names to save space:

| Key | Contents |
|---|---|
| `nomm_v` | Protocol version |
| `nomm_c` | Number of data chunks |
| `nomm_d0`..`nomm_dN` | Modlist JSON, split into 64-byte pieces |
| `nomm_h0`..`nomm_hN` | SHA-256 hash of the full JSON |
| `nomm_r0`..`nomm_rN` | Required mod IDs, split into 64-byte pieces |

## Updating the modpack

Drop a new `.nommpack` into the `modpacks/` folder and NOSMR picks it up
automatically (with a short debounce to avoid rapid republishing). You can also
delete or rename the file. No server restart needed.

## Troubleshooting

**NOMM shows "No data" for my server**

Make sure `NOSMR.dll` is in the right place and check the BepInEx log for
`[NOSMR] Published X mod(s) to A2S_RULES`. If you see that, the plugin is
working and the issue is likely on the client side (NOMM's A2S query can be
intermittent - it retries up to 3 times).

**NOMM shows the mods but says they're all "Not In Manifest"**

Your `modlist.nomm.json` IDs probably don't match the NOMN manifest. Check the
exact IDs - they're case-sensitive in the manifest.

**No mods found in .nommpack files**

Make sure the `.nommpack` files are in the `modpacks/` folder next to
`NOSMR.dll`, and that the ZIP contains a file named exactly `modlist.nomm.json`.

## Building from source

```
dotnet build
```

Needs .NET Framework 4.8 targeting pack. The build pulls in BepInEx,
steamworks4j, and UnityEngine from NuGet. No external JSON library needed.

## License

MIT
