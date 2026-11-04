using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cartovia.GeoJson;
using Cartovia.Mapbox.Services.Internal;

namespace Cartovia.Mapbox.Services;

/// <summary>
/// Base of the place features returned by Geocoding v6 and the Search Box API: the GeoJSON <see cref="Feature"/>
/// (ready to be added to a map source) plus typed accessors over its properties.
/// </summary>
public abstract class PlaceFeature
{
    /// <summary>The underlying GeoJSON point feature.</summary>
    [JsonIgnore] public Feature Feature { get; internal set; } = new();

    /// <summary>Persistent Mapbox identifier of the feature.</summary>
    public string? MapboxId { get; init; }

    /// <summary>Feature type: <c>country</c>, <c>region</c>, <c>postcode</c>, <c>district</c>, <c>place</c>, <c>locality</c>,
    /// <c>neighborhood</c>, <c>street</c>, <c>address</c>, <c>poi</c>…</summary>
    public string? FeatureType { get; init; }

    /// <summary>Name of the feature (e.g. <c>974 2100 South</c>, <c>Paris</c>).</summary>
    public string? Name { get; init; }

    /// <summary>Name in the local language or the first requested language.</summary>
    public string? NamePreferred { get; init; }

    /// <summary>Formatted context (e.g. <c>Salt Lake City, Utah 84106, United States</c>).</summary>
    public string? PlaceFormatted { get; init; }

    /// <summary>Full formatted address, when available.</summary>
    public string? FullAddress { get; init; }

    /// <summary>Coordinates, accuracy and routable points.</summary>
    public PlaceCoordinates? Coordinates { get; init; }

    /// <summary>Bounding box of the feature (administrative areas).</summary>
    [JsonPropertyName("bbox")] public LngLatBounds? BBox { get; init; }

    /// <summary>Hierarchy of parent features.</summary>
    public PlaceContext? Context { get; init; }

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Location of the feature.</summary>
    [JsonIgnore]
    public LngLat Location => Coordinates is { } c
        ? new LngLat(c.Longitude, c.Latitude)
        : Feature.Geometry is Point p ? p.Coordinates : default;

    /// <summary>Best display label: <see cref="FullAddress"/>, else <c>Name, PlaceFormatted</c>.</summary>
    [JsonIgnore]
    public string DisplayName => FullAddress
        ?? string.Join(", ", new[] { Name, PlaceFormatted }.Where(s => !string.IsNullOrEmpty(s)));

    /// <inheritdoc />
    public override string ToString() => DisplayName;

    internal static T Create<T>(Feature feature) where T : PlaceFeature, new()
    {
        var result = feature.Properties.Deserialize<T>(ServicesJson.Options) ?? new T();
        result.Feature = feature;
        return result;
    }
}

/// <summary>Coordinates of a place.</summary>
public sealed class PlaceCoordinates
{
    /// <summary>Longitude.</summary>
    public double Longitude { get; init; }

    /// <summary>Latitude.</summary>
    public double Latitude { get; init; }

    /// <summary>Accuracy of an address point: <c>rooftop</c>, <c>parcel</c>, <c>point</c>, <c>interpolated</c>, <c>approximate</c>, <c>street</c>.</summary>
    public string? Accuracy { get; init; }

    /// <summary>Points to navigate to (e.g. the entrance).</summary>
    public IReadOnlyList<RoutablePoint>? RoutablePoints { get; init; }

    /// <summary>The coordinate as <see cref="LngLat"/>.</summary>
    [JsonIgnore] public LngLat Location => new(Longitude, Latitude);
}

/// <summary>A routable point of a place.</summary>
public sealed class RoutablePoint
{
    /// <summary>Name (e.g. <c>default</c>, <c>POI</c>).</summary>
    public string? Name { get; init; }

    /// <summary>Longitude.</summary>
    public double Longitude { get; init; }

    /// <summary>Latitude.</summary>
    public double Latitude { get; init; }

    /// <summary>The coordinate as <see cref="LngLat"/>.</summary>
    [JsonIgnore] public LngLat Location => new(Longitude, Latitude);
}

/// <summary>Hierarchy of the features containing a place.</summary>
public sealed class PlaceContext
{
    /// <summary>Country.</summary>
    public ContextEntry? Country { get; init; }
    /// <summary>Region (state, province).</summary>
    public ContextEntry? Region { get; init; }
    /// <summary>Postal code.</summary>
    public ContextEntry? Postcode { get; init; }
    /// <summary>District (county).</summary>
    public ContextEntry? District { get; init; }
    /// <summary>Place (city, town, village).</summary>
    public ContextEntry? Place { get; init; }
    /// <summary>Locality.</summary>
    public ContextEntry? Locality { get; init; }
    /// <summary>Neighborhood.</summary>
    public ContextEntry? Neighborhood { get; init; }
    /// <summary>Street.</summary>
    public ContextEntry? Street { get; init; }
    /// <summary>Address.</summary>
    public ContextEntry? Address { get; init; }
    /// <summary>Block (Japan).</summary>
    public ContextEntry? Block { get; init; }
    /// <summary>Secondary address (US units).</summary>
    public ContextEntry? SecondaryAddress { get; init; }

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>An entry of a <see cref="PlaceContext"/>.</summary>
public sealed class ContextEntry
{
    /// <summary>Mapbox identifier.</summary>
    public string? MapboxId { get; init; }
    /// <summary>Name.</summary>
    public string? Name { get; init; }
    /// <summary>Wikidata identifier.</summary>
    public string? WikidataId { get; init; }
    /// <summary>ISO 3166-1 alpha-2 country code (country).</summary>
    public string? CountryCode { get; init; }
    /// <summary>ISO 3166-1 alpha-3 country code (country).</summary>
    [JsonPropertyName("country_code_alpha_3")] public string? CountryCodeAlpha3 { get; init; }
    /// <summary>Region code, e.g. <c>UT</c> (region).</summary>
    public string? RegionCode { get; init; }
    /// <summary>Full ISO 3166-2 region code, e.g. <c>US-UT</c> (region).</summary>
    public string? RegionCodeFull { get; init; }
    /// <summary>House number (address).</summary>
    public string? AddressNumber { get; init; }
    /// <summary>Street name (address).</summary>
    public string? StreetName { get; init; }

    /// <summary>Members not mapped to a property (translations, alternate…).</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}
