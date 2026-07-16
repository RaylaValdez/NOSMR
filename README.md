# NOSMR

Server-side BepInEx plugin that broadcasts your modlist via A2S_RULES so NOMM clients know what mods you're running.

## Install

Drop `NOSMR.dll` into `BepInEx/plugins/`. Create a `modpacks/` folder next to it and put your `.nommpack` files there.

```
BepInEx/plugins/NOSMR.dll
BepInEx/plugins/modpacks/
  my-pack.nommpack
```

## Config

`BepInEx/config/gerryofravine.nosmr.cfg`

```ini
[General]
Enabled = true
RequiredMods =

[Debug]
EnableDebugLog = false
```

## Building

```
dotnet build
```

Requires .NET Framework 4.8 targeting pack.
