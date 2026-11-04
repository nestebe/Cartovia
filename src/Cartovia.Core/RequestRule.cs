using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Cartovia.Json;

namespace Cartovia;

/// <summary>Kind of resource requested by the map engine (matches <c>transformRequest</c> resource types).</summary>
public enum RequestResourceType
{
    /// <summary>Style JSON.</summary>
    [JsonValue("Style")] Style,
    /// <summary>TileJSON of a source.</summary>
    [JsonValue("Source")] Source,
    /// <summary>Vector or raster tile.</summary>
    [JsonValue("Tile")] Tile,
    /// <summary>Glyph (font) range.</summary>
    [JsonValue("Glyphs")] Glyphs,
    /// <summary>Sprite image.</summary>
    [JsonValue("SpriteImage")] SpriteImage,
    /// <summary>Sprite JSON.</summary>
    [JsonValue("SpriteJSON")] SpriteJson,
    /// <summary>Image (image source, <c>loadImage</c>).</summary>
    [JsonValue("Image")] Image,
}

/// <summary>Credentials mode of matching requests.</summary>
public enum RequestCredentials
{
    /// <summary>Send cookies to the same origin only.</summary>
    [JsonValue("same-origin")] SameOrigin,
    /// <summary>Send cookies to every origin (the server must allow credentials).</summary>
    [JsonValue("include")] Include,
}

/// <summary>
/// Modifies the requests made by the map engine (tiles, styles, sprites, glyphs…) whose URL matches a pattern:
/// adds headers (e.g. <c>Authorization</c>), query parameters (e.g. an API key) or credentials.
/// Configure rules with <see cref="MapOptions.RequestRules"/> and update them at any time with
/// <see cref="CartoviaMap.SetRequestRulesAsync"/> (e.g. to refresh an expiring token).
/// </summary>
/// <remarks>
/// Custom headers turn tile requests into CORS "non-simple" requests: the tile server must answer the preflight
/// (<c>Access-Control-Allow-Headers</c>). Query parameters do not have this constraint.
/// </remarks>
/// <example><code>
/// options.RequestRules.Add(new RequestRule("https://tiles.example.com/*")
///     .WithHeader("Authorization", "Bearer " + token));
/// </code></example>
public sealed class RequestRule
{
    /// <summary>Creates a rule.</summary>
    /// <param name="urlPattern">
    /// URL pattern: <c>*</c> matches any characters (case-insensitive, whole URL), e.g. <c>https://tiles.example.com/*</c>.
    /// Prefix with <c>regex:</c> to use a JavaScript regular expression, e.g. <c>regex:^https://[a-c]\.tiles\.example\.com/</c>.
    /// </param>
    public RequestRule(string urlPattern)
    {
        if (string.IsNullOrWhiteSpace(urlPattern)) throw new ArgumentException("A URL pattern is required.", nameof(urlPattern));
        UrlPattern = urlPattern;
    }

    /// <summary>URL pattern (glob with <c>*</c>, or <c>regex:</c> prefix).</summary>
    public string UrlPattern { get; }

    /// <summary>Headers added to matching requests.</summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Query parameters appended to matching URLs.</summary>
    public IDictionary<string, string> QueryParameters { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Credentials mode of matching requests.</summary>
    public RequestCredentials? Credentials { get; set; }

    /// <summary>Restricts the rule to some resource types. Empty: every type.</summary>
    public ISet<RequestResourceType> ResourceTypes { get; } = new HashSet<RequestResourceType>();

    /// <summary>Adds a header (fluent).</summary>
    public RequestRule WithHeader(string name, string value) { Headers[name] = value; return this; }

    /// <summary>Adds a query parameter (fluent).</summary>
    public RequestRule WithQueryParameter(string name, string value) { QueryParameters[name] = value; return this; }

    /// <summary>Restricts the rule to resource types (fluent).</summary>
    public RequestRule ForResourceTypes(params RequestResourceType[] types)
    {
        foreach (var t in types) ResourceTypes.Add(t);
        return this;
    }

    internal JsonObject ToJson()
    {
        var o = new JsonObject { ["pattern"] = UrlPattern };
        if (Headers.Count > 0) o["headers"] = new JsonObject(Headers.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value)));
        if (QueryParameters.Count > 0) o["query"] = new JsonObject(QueryParameters.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value)));
        if (Credentials is { } c) o["credentials"] = CartoviaJson.EnumToString(c);
        if (ResourceTypes.Count > 0) o["types"] = new JsonArray(ResourceTypes.Select(t => (JsonNode?)JsonValue.Create(CartoviaJson.EnumToString(t))).ToArray());
        return o;
    }

    internal static JsonArray ToJson(IEnumerable<RequestRule> rules) => new(rules.Select(r => (JsonNode?)r.ToJson()).ToArray());
}
