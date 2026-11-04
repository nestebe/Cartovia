using System;
using System.IO;

namespace Cartovia.Bridge;

/// <summary>
/// Optional capability of an <see cref="IMapHost"/>: serves data (large GeoJSON, PMTiles, images…) to the page from
/// .NET through URLs of the page origin, instead of sending it inside JSON messages. The engine then downloads and
/// parses the data in a web worker, which keeps the UI responsive with large datasets.
/// </summary>
public interface IMapDataHost
{
    /// <summary>Publishes data and returns its absolute URL (e.g. <c>https://cartovia.local/data/name.json</c>).</summary>
    /// <param name="name">Unique name (letters, digits, <c>-</c>, <c>_</c>, <c>.</c>, <c>/</c>).</param>
    /// <param name="open">Opens a new stream of the data for every request.</param>
    /// <param name="contentType">MIME type.</param>
    string PublishData(string name, Func<Stream> open, string contentType);

    /// <summary>Stops serving published data.</summary>
    void UnpublishData(string name);
}
