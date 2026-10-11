using System;
using System.Diagnostics.CodeAnalysis;

namespace PKHeX.Core;

/// <summary>
/// Recognizes and loads Relumi (.pbrelumi) JSON save files.
/// Checks for the presence of the "playerWork" key as a heuristic.
/// </summary>
public sealed class PbrelumiReader : ISaveReader
{
    public bool IsRecognized(long dataLength) => dataLength > 16;

    public bool TryRead(Memory<byte> data, [NotNullWhen(true)] out SaveFile? result, string? path = null)
    {
        result = null;

        // Quick heuristic: check the first bytes for JSON object start '{' and "playerWork"
        var span = data.Span;
        if (span.Length < 16 || span[0] != '{')
            return false;

        // Look for "playerWork" near the start
        int playerWorkIndex = span.IndexOf("\"playerWork\""u8);
        if (playerWorkIndex < 0 || playerWorkIndex > 100)
            return false;

        try
        {
            result = new SAVRelumi(data);
            // Set the metadata file path if provided
            if (path is not null)
                result.Metadata.SetExtraInfo(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}