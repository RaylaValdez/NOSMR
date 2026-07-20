# NOSMR - Nuclear Option Server Mod Reporter

Server-side BepInEx plugin that broadcasts your modlist via A2S_RULES so [NOMM](https://github.com/Combat787/NOMM) clients know what mods you're running.

NOSMR also enables [NOMM](https://github.com/Combat787/NOMM)'s automatic connect features.

## Install

Create a folder called `NOSMR` in `BepInEx/plugins/`. Drop the `NOSMR.dll` in it.
Create a folder inside `NOSMR` called `modpacks`, place only one .nommpack file at a time in that folder.

```
BepInEx/plugins/NOSMR/NOSMR.dll
BepInEx/plugins/NOSMR/modpacks/
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
