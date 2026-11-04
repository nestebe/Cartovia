using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mapbox.Net.Services.Tests;

internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body, string? ContentType);

/// <summary>Records requests and returns canned responses. Never touches the network.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<CapturedRequest> Requests { get; } = new();

    public CapturedRequest Last => Requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, request.Content?.Headers.ContentType?.MediaType));
        return _responder(request);
    }
}

internal static class TestClient
{
    public const string Token = "pk.eyJ1IjoidGVzdCJ9.secret-token";

    public static (MapboxClient Client, FakeHandler Handler) Create(string json, HttpStatusCode status = HttpStatusCode.OK,
        Action<HttpResponseMessage>? configure = null, Uri? baseUrl = null)
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });
        var options = new MapboxClientOptions(Token);
        if (baseUrl is not null) options.BaseUrl = baseUrl;
        return (new MapboxClient(options, new HttpClient(handler)), handler);
    }

    public static (MapboxClient Client, FakeHandler Handler) CreateBytes(byte[] bytes, string contentType)
    {
        var handler = new FakeHandler(_ =>
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        return (new MapboxClient(Token, new HttpClient(handler)), handler);
    }

    /// <summary>Decoded query (or form) parameters.</summary>
    public static Dictionary<string, string> Params(string? query)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            result[Uri.UnescapeDataString(part[..i])] = Uri.UnescapeDataString(part[(i + 1)..]);
        }
        return result;
    }

    public static Dictionary<string, string> Params(Uri uri) => Params(uri.Query);
}

/// <summary>Runs a block with a culture that uses ',' as decimal separator.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture, _uiCulture;

    public CultureScope(string name = "fr-FR")
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
