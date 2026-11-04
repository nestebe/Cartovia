using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Mapbox.Net.Services;

/// <summary>
/// Error returned by a Mapbox web service: a non-success HTTP status, or a navigation response
/// whose <c>code</c> is not <c>Ok</c> (for example <c>NoRoute</c>, <c>NoMatch</c>, <c>NoTrips</c>).
/// The access token is never included in the message or in <see cref="RequestPath"/>.
/// </summary>
public class MapboxApiException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="message">Exception message.</param>
    /// <param name="apiMessage">The <c>message</c> member returned by Mapbox, if any.</param>
    /// <param name="errorCode">The Mapbox error code (<c>code</c> / <c>error_code</c>), if any.</param>
    /// <param name="requestPath">The request path (without host and query string).</param>
    /// <param name="rateLimit">Rate-limit information read from the response headers.</param>
    /// <param name="responseBody">Raw response body (truncated), token redacted.</param>
    public MapboxApiException(HttpStatusCode statusCode, string message, string? apiMessage = null, string? errorCode = null,
        string? requestPath = null, MapboxRateLimit? rateLimit = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage;
        ErrorCode = errorCode;
        RequestPath = requestPath;
        RateLimit = rateLimit;
        ResponseBody = responseBody;
    }

    /// <summary>HTTP status code of the response (200 for navigation responses with a non-<c>Ok</c> code).</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The <c>message</c> returned by Mapbox, if any.</summary>
    public string? ApiMessage { get; }

    /// <summary>The Mapbox error code, e.g. <c>NoRoute</c>, <c>InvalidInput</c>, <c>NoSegment</c>, <c>ProfileNotFound</c>.</summary>
    public string? ErrorCode { get; }

    /// <summary>The request path (e.g. <c>/directions/v5/mapbox/driving/…</c>), without query string or token.</summary>
    public string? RequestPath { get; }

    /// <summary>Rate-limit headers of the response, when present.</summary>
    public MapboxRateLimit? RateLimit { get; }

    /// <summary>Raw response body (truncated to 4 KB), with the access token redacted.</summary>
    public string? ResponseBody { get; }

    /// <summary>True when the request was rejected because of rate limiting (HTTP 429).</summary>
    public bool IsRateLimited => (int)StatusCode == 429;

    /// <summary>True for authentication / authorization failures (HTTP 401 or 403).</summary>
    public bool IsUnauthorized => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

/// <summary>Rate-limit information from the <c>X-Rate-Limit-*</c> and <c>Retry-After</c> response headers.</summary>
public sealed class MapboxRateLimit
{
    /// <summary>Length of the rate-limit window (<c>X-Rate-Limit-Interval</c>).</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>Maximum number of requests per window (<c>X-Rate-Limit-Limit</c>).</summary>
    public int? Limit { get; init; }

    /// <summary>When the current window resets (<c>X-Rate-Limit-Reset</c>, a Unix timestamp).</summary>
    public DateTimeOffset? Reset { get; init; }

    /// <summary>Delay suggested by the <c>Retry-After</c> header, if any.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Reads the rate-limit headers, or returns null if none is present.</summary>
    public static MapboxRateLimit? FromHeaders(HttpResponseHeaders headers)
    {
        if (headers is null) throw new ArgumentNullException(nameof(headers));
        long? interval = ReadLong(headers, "X-Rate-Limit-Interval");
        long? limit = ReadLong(headers, "X-Rate-Limit-Limit");
        long? reset = ReadLong(headers, "X-Rate-Limit-Reset");
        TimeSpan? retryAfter = headers.RetryAfter switch
        {
            { Delta: { } d } => d,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => null,
        };
        if (interval is null && limit is null && reset is null && retryAfter is null) return null;
        return new MapboxRateLimit
        {
            Interval = interval is { } i ? TimeSpan.FromSeconds(i) : null,
            Limit = limit is { } l ? (int)Math.Min(l, int.MaxValue) : null,
            Reset = reset is { } r ? DateTimeOffset.FromUnixTimeSeconds(r) : null,
            RetryAfter = retryAfter,
        };
    }

    private static long? ReadLong(HttpResponseHeaders headers, string name)
        => headers.TryGetValues(name, out var values)
           && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : null;

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"Limit={Limit}, Interval={Interval}, Reset={Reset:O}, RetryAfter={RetryAfter}");
}
