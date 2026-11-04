using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cartovia.Mapbox.Services.Internal;

/// <summary>Shared HTTP transport: builds URLs, appends the access token, maps errors to <see cref="MapboxApiException"/>.</summary>
internal sealed class MapboxHttp
{
    /// <summary>Above this URL length, APIs that support it are called with POST.</summary>
    public const int MaxGetUrlLength = 8000;

    private const int MaxBodyInException = 4096;

    private static readonly ProductInfoHeaderValue UserAgent = new(
        "Cartovia.Mapbox.Services",
        typeof(MapboxHttp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0");

    private readonly HttpClient _client;
    private readonly string _accessToken;

    public MapboxHttp(HttpClient client, string accessToken, Uri baseUri)
    {
        _client = client;
        _accessToken = accessToken;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }

    /// <summary>Builds an absolute URL. <paramref name="path"/> must already be percent-encoded.</summary>
    public Uri BuildUri(string path, QueryBuilder? query, bool includeToken = true)
    {
        var sb = new StringBuilder(BaseUri.AbsoluteUri.TrimEnd('/'));
        sb.Append('/').Append(path.TrimStart('/'));
        var q = query?.ToString() ?? string.Empty;
        if (includeToken)
            q = (q.Length == 0 ? string.Empty : q + "&") + "access_token=" + Uri.EscapeDataString(_accessToken);
        if (q.Length > 0) sb.Append('?').Append(q);
        return new Uri(sb.ToString(), UriKind.Absolute);
    }

    /// <summary>Length of the URL that a GET request would use.</summary>
    public int GetUrlLength(string path, QueryBuilder query) => BuildUri(path, query).AbsoluteUri.Length;

    public Task<T> GetJsonAsync<T>(string path, QueryBuilder? query, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, query));
        return SendJsonAsync(request, path, parse, cancellationToken);
    }

    public Task<T> PostFormAsync<T>(string path, QueryBuilder form, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path, null))
        {
            Content = new StringContent(form.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        return SendJsonAsync(request, path, parse, cancellationToken);
    }

    public Task<T> PostJsonAsync<T>(string path, QueryBuilder? query, string json, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path, query))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return SendJsonAsync(request, path, parse, cancellationToken);
    }

    public async Task<byte[]> GetBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(request, uri.AbsolutePath, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendJsonAsync<T>(HttpRequestMessage request, string path, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await SendAsync(request, path, cancellationToken).ConfigureAwait(false);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            return parse(document.RootElement);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
    {
        if (_client.DefaultRequestHeaders.UserAgent.Count == 0) request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            string body = string.Empty;
            try { body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false); }
            catch (HttpRequestException) { /* body unavailable */ }
            throw CreateException(response.StatusCode, response.ReasonPhrase, body, path, MapboxRateLimit.FromHeaders(response.Headers));
        }
    }

    internal MapboxApiException CreateException(HttpStatusCode status, string? reason, string body, string path, MapboxRateLimit? rateLimit)
    {
        path = "/" + path.TrimStart('/');
        body = Redact(body);
        string? apiMessage = null, code = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            apiMessage = ServicesJson.GetString(root, "message") ?? ServicesJson.GetString(root, "error");
            code = ServicesJson.GetString(root, "code") ?? ServicesJson.GetString(root, "error_code");
        }
        catch (JsonException) { /* not JSON */ }

        var sb = new StringBuilder();
        sb.Append("Mapbox request ").Append(Shorten(path)).Append(" failed with HTTP ").Append((int)status);
        if (!string.IsNullOrEmpty(reason)) sb.Append(' ').Append(reason);
        if (!string.IsNullOrEmpty(apiMessage)) sb.Append(": ").Append(apiMessage);
        if (!string.IsNullOrEmpty(code)) sb.Append(" (code: ").Append(code).Append(')');
        if (rateLimit?.RetryAfter is { } retry) sb.Append(". Retry after ").Append(Math.Ceiling(retry.TotalSeconds)).Append(" s");

        return new MapboxApiException(status, sb.ToString(), apiMessage, code, Redact(path), rateLimit,
            body.Length > MaxBodyInException ? body.Substring(0, MaxBodyInException) : body);
    }

    /// <summary>Throws when a navigation response has a <c>code</c> other than <c>Ok</c>.</summary>
    public void EnsureOk(JsonElement root, string path)
    {
        path = "/" + path.TrimStart('/');
        var code = ServicesJson.GetString(root, "code");
        if (code is null || string.Equals(code, "Ok", StringComparison.OrdinalIgnoreCase)) return;
        var apiMessage = ServicesJson.GetString(root, "message");
        var message = $"Mapbox request {Shorten(path)} returned code '{code}'" + (apiMessage is null ? "." : ": " + apiMessage);
        throw new MapboxApiException(HttpStatusCode.OK, Redact(message), apiMessage is null ? null : Redact(apiMessage), code, Redact(path));
    }

    private string Redact(string text)
        => string.IsNullOrEmpty(text) || _accessToken.Length == 0 ? text : text.Replace(_accessToken, "***");

    private static string Shorten(string path) => path.Length <= 160 ? path : path.Substring(0, 157) + "...";
}
