using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NOSMR;

/// <summary>
/// Shared constants and utilities for the NOMM protocol.
/// Used by both A2SRulesPublisher and LobbyDataPublisher.
/// </summary>
internal static class NommProtocol
{
    internal const int ChunkSize = 64;

    internal const string KeyVersion = "nomm_v";
    internal const string KeyChunkCount = "nomm_c";
    internal const string KeyDataPrefix = "nomm_d";
    internal const string KeyHashPrefix = "nomm_h";
    internal const string KeyRequiredPrefix = "nomm_r";

    internal const string ProtocolVersion = "2";

    internal static string ComputeModHashPrefix(string id, string version)
    {
        using (var sha = SHA256.Create())
        {
            var input = id + "|" + version;
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(6);
            for (int i = 0; i < 3; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }

    internal static List<string> SplitIntoChunks(string value)
    {
        var chunks = new List<string>();
        for (int i = 0; i < value.Length; i += ChunkSize)
        {
            int length = Math.Min(ChunkSize, value.Length - i);
            chunks.Add(value.Substring(i, length));
        }
        return chunks;
    }

    internal static string ComputeDataHash(string data)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
            var sb = new StringBuilder("sha256:");
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
