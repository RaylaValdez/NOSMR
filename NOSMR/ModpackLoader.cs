using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace NOSMR;

/// <summary>
/// Reads .nommpack files from the modpacks/ directory and extracts modlists.
/// Each .nommpack is a ZIP archive containing modlist.nomm.json at the root.
/// </summary>
public class ModpackLoader
{
    private readonly FileLogger? _logger;

    public ModpackLoader(FileLogger? logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load all .nommpack files from the given directory and return a combined modlist.
    /// When multiple .nommpack files exist, entries are merged by id (last version wins).
    /// </summary>
    public List<PackageReference> Load(string modpacksDirectory)
    {
        if (!Directory.Exists(modpacksDirectory))
        {
            _logger?.Warn($"Modpacks directory not found: {modpacksDirectory}");
            return new List<PackageReference>();
        }

        var packFiles = Directory.GetFiles(modpacksDirectory, "*.nommpack");
        if (packFiles.Length == 0)
        {
            _logger?.Info("No .nommpack files found in modpacks directory");
            return new List<PackageReference>();
        }

        _logger?.Info($"Found {packFiles.Length} .nommpack file(s)");

        var merged = new Dictionary<string, PackageReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var packFile in packFiles)
        {
            try
            {
                var mods = ReadNommpack(packFile);
                _logger?.Info($"  {Path.GetFileName(packFile)}: {mods.Count} mod(s)");

                foreach (var mod in mods)
                {
                    merged[mod.Id] = mod;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to read {Path.GetFileName(packFile)}", ex);
            }
        }

        var result = merged.Values.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
        _logger?.Info($"Combined modlist: {result.Count} unique mod(s)");
        return result;
    }

    private List<PackageReference> ReadNommpack(string filePath)
    {
        using (var stream = File.OpenRead(filePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            var entry = archive.GetEntry("modlist.nomm.json");
            if (entry == null)
                throw new InvalidOperationException("modlist.nomm.json not found in archive");

            using (var entryStream = entry.Open())
            using (var reader = new StreamReader(entryStream))
            {
                var json = reader.ReadToEnd();
                return PackageReference.DeserializeList(json);
            }
        }
    }
}
