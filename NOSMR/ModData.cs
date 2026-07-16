using System;
using System.Collections.Generic;
using System.Text;

namespace NOSMR;

/// <summary>
/// Matches NOMM's PackageReference format exactly.
/// Used in modlist.nomm.json and A2S_RULES nomm_data.
/// </summary>
public class PackageReference
{
    public string Id { get; set; } = string.Empty;
    public string? Version { get; set; }

    public PackageReference() { }

    public PackageReference(string id, string? version)
    {
        Id = id;
        Version = version;
    }

    public override string ToString() =>
        Version != null ? $"{Id}@{Version}" : Id;

    public static string SerializeList(List<PackageReference> modlist)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < modlist.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":\"");
            sb.Append(Escape(modlist[i].Id ?? string.Empty));
            sb.Append('"');
            if (modlist[i].Version != null)
            {
                sb.Append(",\"version\":\"");
                sb.Append(Escape(modlist[i].Version!));
                sb.Append('"');
            }
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static List<PackageReference> DeserializeList(string json)
    {
        var result = new List<PackageReference>();
        json = json.Trim();
        if (json.Length < 2 || json[0] != '[' || json[json.Length - 1] != ']')
            return result;

        int pos = 1;
        while (pos < json.Length)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= json.Length || json[pos] == ']') break;
            if (json[pos] == ',') { pos++; continue; }
            if (json[pos] != '{') break;

            string? id = null, version = null;
            pos++; // skip {

            while (pos < json.Length && json[pos] != '}')
            {
                while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
                if (pos >= json.Length || json[pos] == '}') break;

                // parse key
                if (json[pos] != '"') break;
                pos++; // skip opening quote
                var keyStart = pos;
                while (pos < json.Length && json[pos] != '"')
                    if (json[pos] == '\\') pos += 2; else pos++;
                var key = Unescape(json.Substring(keyStart, pos - keyStart));
                pos++; // skip closing quote

                // skip : and whitespace
                while (pos < json.Length && (json[pos] == ':' || char.IsWhiteSpace(json[pos]))) pos++;

                // parse value
                if (pos >= json.Length) break;
                if (json[pos] == '"')
                {
                    pos++; // skip opening quote
                    var valStart = pos;
                    while (pos < json.Length && json[pos] != '"')
                        if (json[pos] == '\\') pos += 2; else pos++;
                    var val = Unescape(json.Substring(valStart, pos - valStart));
                    pos++; // skip closing quote

                    if (key == "id") id = val;
                    else if (key == "version") version = val;
                }
                else
                {
                    // skip non-string value (null, number, bool)
                    while (pos < json.Length && json[pos] != ',' && json[pos] != '}') pos++;
                }

                while (pos < json.Length && (json[pos] == ',' || char.IsWhiteSpace(json[pos]))) pos++;
            }

            if (pos < json.Length && json[pos] == '}') pos++;
            if (id != null)
                result.Add(new PackageReference(id, version));
        }

        return result;
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string Unescape(string s)
    {
        if (!s.Contains("\\")) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                switch (s[++i])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(s[i]); break;
                }
            }
            else
                sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
