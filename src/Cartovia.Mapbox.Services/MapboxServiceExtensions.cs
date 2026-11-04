using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cartovia.Mapbox.Services;

/// <summary>Shortcuts for the most common calls.</summary>
public static class MapboxServiceExtensions
{
    /// <summary>Forward-geocodes a free-text query.</summary>
    public static Task<GeocodingResponse> ForwardAsync(this IGeocodingService service, string query, int? limit = null, CancellationToken cancellationToken = default)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        return service.ForwardAsync(new ForwardGeocodingRequest(query) { Limit = limit }, cancellationToken);
    }

    /// <summary>Reverse-geocodes a location.</summary>
    public static Task<GeocodingResponse> ReverseAsync(this IGeocodingService service, LngLat location, CancellationToken cancellationToken = default)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        return service.ReverseAsync(new ReverseGeocodingRequest(location), cancellationToken);
    }

    /// <summary>Computes a route through the given coordinates with default options.</summary>
    public static Task<DirectionsResponse> GetRouteAsync(this IDirectionsService service, RoutingProfile profile, IEnumerable<LngLat> coordinates, CancellationToken cancellationToken = default)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (coordinates is null) throw new ArgumentNullException(nameof(coordinates));
        return service.GetRouteAsync(new DirectionsRequest(profile, coordinates.ToArray()), cancellationToken);
    }

    /// <summary>Computes isochrones for the given travel times in minutes (polygons).</summary>
    public static Task<IsochroneResponse> GetIsochronesAsync(this IIsochroneService service, RoutingProfile profile, LngLat center, IEnumerable<int> minutes, CancellationToken cancellationToken = default)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (minutes is null) throw new ArgumentNullException(nameof(minutes));
        return service.GetIsochronesAsync(new IsochroneRequest(profile, center) { ContoursMinutes = minutes.ToList() }, cancellationToken);
    }
}
