using Newtonsoft.Json;

namespace NOSMR;

/// <summary>
/// Matches NOMM's PackageReference format exactly.
/// Used in modlist.nomm.json and A2S_RULES nomm_data.
/// </summary>
public class PackageReference
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)]
    public string? Version { get; set; }

    public PackageReference() { }

    public PackageReference(string id, string? version)
    {
        Id = id;
        Version = version;
    }

    public override string ToString() =>
        Version != null ? $"{Id}@{Version}" : Id;
}
