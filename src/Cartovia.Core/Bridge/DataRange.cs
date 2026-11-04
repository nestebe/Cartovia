using System;
using System.Globalization;
using System.IO;

namespace Cartovia.Bridge;

/// <summary>HTTP <c>Range</c> helpers for hosts serving published data (PMTiles and other range readers).</summary>
public static class DataRange
{
    /// <summary>
    /// Parses a single <c>bytes=</c> range against a seekable stream.
    /// </summary>
    /// <returns>
    /// False when the request is not a usable range request (serve the whole stream). True with <paramref name="start"/>
    /// = -1 when the range cannot be satisfied (answer 416).
    /// </returns>
    public static bool TryParse(string? range, Stream stream, out long start, out long end, out long total)
    {
        start = end = total = 0;
        if (range is null || !stream.CanSeek || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        total = stream.Length;
        var spec = range.Substring(6).Split(',')[0].Trim().Split('-');
        if (spec.Length != 2) return false;
        try
        {
            if (spec[0].Length == 0)
            {
                long suffix = long.Parse(spec[1], CultureInfo.InvariantCulture);
                start = Math.Max(0, total - suffix);
                end = total - 1;
            }
            else
            {
                start = long.Parse(spec[0], CultureInfo.InvariantCulture);
                end = spec[1].Length > 0 ? Math.Min(long.Parse(spec[1], CultureInfo.InvariantCulture), total - 1) : total - 1;
            }
        }
        catch (FormatException) { return false; }
        if (start >= total || end < start) start = -1;
        return true;
    }

    /// <summary>Reads <c>[start, end]</c> from a seekable stream and disposes it.</summary>
    public static byte[] Read(Stream stream, long start, long end)
    {
        using (stream)
        {
            var buffer = new byte[end - start + 1];
            stream.Seek(start, SeekOrigin.Begin);
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read == buffer.Length) return buffer;
            Array.Resize(ref buffer, read);
            return buffer;
        }
    }
}
