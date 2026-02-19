using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using local_translate_provider.ApiAdapters;
using local_translate_provider.Models;
using local_translate_provider.Services;

namespace local_translate_provider.Services;

/// <summary>
/// HTTP server that exposes DeepL and Google Translate format endpoints.
/// Uses TcpListener for single-port handling: CONNECT (proxy) and direct HTTP.
/// </summary>
public sealed class HttpTranslationServer
{
    private static readonly HashSet<string> InterceptedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api-free.deepl.com",
        "api.deepl.com",
        "translate.googleapis.com"
    };

    private readonly AppSettings _settings;
    private readonly TranslationService _translationService;
    private readonly CertificateManager _certManager;
    private TcpListener? _listener;
    private Task? _runTask;
    private readonly object _lock = new();

    public HttpTranslationServer(AppSettings settings, TranslationService translationService, CertificateManager certManager)
    {
        _settings = settings;
        _translationService = translationService;
        _certManager = certManager;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_listener != null) return;

            var port = _settings.Port;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _runTask = RunAsync();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _listener?.Stop();
            _listener = null;
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private async Task RunAsync()
    {
        while (_listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = HandleConnectionAsync(client);
            }
            catch (ObjectDisposedException) { break; }
            catch (InvalidOperationException) { break; }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var firstByte = stream.ReadByte();
                if (firstByte < 0) return;

                if (firstByte == 'C')
                {
                    await HandleProxyAsync(stream, new byte[] { (byte)firstByte }).ConfigureAwait(false);
                    return;
                }
                if (firstByte == 'P' || firstByte == 'G')
                {
                    var buffer = new byte[1] { (byte)firstByte };
                    await HandleHttpAsync(stream, buffer).ConfigureAwait(false);
                    return;
                }
            }
        }
        catch { /* Connection closed or error */ }
    }

    private async Task HandleProxyAsync(Stream rawStream, byte[] prefix)
    {
        var (method, path, host, headers, body) = await ParseHttpRequestAsync(rawStream, prefix).ConfigureAwait(false);

        if (method != "CONNECT" || string.IsNullOrEmpty(host))
        {
            await WriteHttpErrorAsync(rawStream, 400, "Bad Request").ConfigureAwait(false);
            return;
        }

        var hostOnly = host.Contains(':') ? host.Split(':')[0] : host;
        if (!InterceptedHosts.Contains(hostOnly))
        {
            await TunnelConnectAsync(rawStream, host, hostOnly).ConfigureAwait(false);
            return;
        }

        await WriteHttpResponseAsync(rawStream, 200, "Connection Established", Array.Empty<KeyValuePair<string, string>>(), null).ConfigureAwait(false);
        await rawStream.FlushAsync().ConfigureAwait(false);

        var cert = _certManager.GetOrCreateServerCert(hostOnly);
        using var ssl = new SslStream(rawStream, leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions
            {
                ServerCertificate = cert,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }).ConfigureAwait(false);

        var (method2, path2, _, headers2, body2) = await ParseHttpRequestAsync(ssl, null).ConfigureAwait(false);
        await DispatchRequestAsync(ssl, method2, path2, headers2, body2).ConfigureAwait(false);
    }

    private static async Task TunnelConnectAsync(Stream clientStream, string host, string hostOnly)
    {
        var port = 443;
        if (host.Contains(':'))
        {
            var parts = host.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var p))
                port = p;
        }

        using var upstream = new TcpClient();
        try
        {
            await upstream.ConnectAsync(hostOnly, port);
        }
        catch
        {
            await WriteHttpErrorAsync(clientStream, 502, "Bad Gateway").ConfigureAwait(false);
            return;
        }

        await WriteHttpResponseAsync(clientStream, 200, "Connection Established",
            Array.Empty<KeyValuePair<string, string>>(), null).ConfigureAwait(false);
        await clientStream.FlushAsync().ConfigureAwait(false);

        using var upstreamStream = upstream.GetStream();
        using var cts = new CancellationTokenSource();
        var toUpstream = clientStream.CopyToAsync(upstreamStream, cts.Token);
        var toClient = upstreamStream.CopyToAsync(clientStream, cts.Token);
        try
        {
            await Task.WhenAny(toUpstream, toClient).ConfigureAwait(false);
        }
        catch (IOException) { /* Connection closed */ }
        finally
        {
            cts.Cancel();
        }
    }

    private async Task HandleHttpAsync(Stream stream, byte[] prefix)
    {
        var (method, path, _, headers, body) = await ParseHttpRequestAsync(stream, prefix).ConfigureAwait(false);
        await DispatchRequestAsync(stream, method, path, headers, body).ConfigureAwait(false);
    }

    private async Task DispatchRequestAsync(Stream responseStream, string method, string path, IReadOnlyDictionary<string, string> headers, byte[]? body)
    {
        var pathWithoutQuery = path.Contains('?') ? path[..path.IndexOf('?')] : path;
        var pathNorm = pathWithoutQuery.TrimEnd('/');

        if (_settings.EnableGoogleEndpoint && pathNorm.EndsWith("/translate_a/single", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            await HandleGoogleLegacySingleAsync(responseStream, path).ConfigureAwait(false);
            return;
        }

        if (method != "POST")
        {
            await WriteHttpErrorAsync(responseStream, 405, "Method Not Allowed").ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            var auth = headers.TryGetValue("Authorization", out var a) ? a : null;
            if (auth != $"DeepL-Auth-Key {_settings.ApiKey}" && auth != $"Bearer {_settings.ApiKey}")
            {
                await WriteHttpErrorAsync(responseStream, 401, "Unauthorized").ConfigureAwait(false);
                return;
            }
        }

        if (_settings.EnableDeepLEndpoint && pathNorm.EndsWith("/v2/translate", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDeepLCoreAsync(responseStream, body).ConfigureAwait(false);
            return;
        }
        if (_settings.EnableGoogleEndpoint && path.Contains(":translateText", StringComparison.OrdinalIgnoreCase))
        {
            await HandleGoogleCoreAsync(responseStream, body).ConfigureAwait(false);
            return;
        }

        await WriteHttpErrorAsync(responseStream, 404, "Not Found").ConfigureAwait(false);
    }

    private static async Task<(string method, string path, string? host, IReadOnlyDictionary<string, string> headers, byte[]? body)> ParseHttpRequestAsync(Stream stream, byte[]? prefix)
    {
        var buffer = new List<byte>();
        if (prefix != null) buffer.AddRange(prefix);

        var headerEnd = -1;

        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0) break;
            buffer.Add((byte)b);
            if (buffer.Count >= 4)
            {
                var n = buffer.Count;
                if (buffer[n - 4] == 13 && buffer[n - 3] == 10 && buffer[n - 2] == 13 && buffer[n - 1] == 10)
                {
                    headerEnd = buffer.Count;
                    break;
                }
            }
        }

        if (headerEnd < 0)
            return ("", "", null, new Dictionary<string, string>(), null);

        var headerBytes = buffer.Take(headerEnd).ToArray();
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length < 1) return ("", "", null, new Dictionary<string, string>(), null);

        var requestLine = lines[0].Split(' ', 3);
        var method = requestLine.Length >= 1 ? requestLine[0] : "";
        var path = requestLine.Length >= 2 ? requestLine[1] : "";

        var headersDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(':');
            if (idx > 0)
            {
                var key = lines[i][..idx].Trim();
                var value = lines[i][(idx + 1)..].Trim();
                headersDict[key] = value;
            }
        }

        headersDict.TryGetValue("Host", out var host);

        byte[]? body = null;
        if (headersDict.TryGetValue("Content-Length", out var clStr) && int.TryParse(clStr, out var contentLength) && contentLength > 0)
        {
            body = new byte[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var n = await stream.ReadAsync(body, read, contentLength - read).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
        }

        return (method, path, host, headersDict, body);
    }

    private static async Task WriteHttpResponseAsync(Stream stream, int statusCode, string statusText,
        IEnumerable<KeyValuePair<string, string>> headers, byte[]? body)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        foreach (var (k, v) in headers)
            sb.Append($"{k}: {v}\r\n");
        if (body != null)
            sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        if (body != null && body.Length > 0)
            await stream.WriteAsync(body).ConfigureAwait(false);
    }

    private static async Task WriteHttpErrorAsync(Stream stream, int statusCode, string message)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"error\":\"{message}\"}}");
        var headers = new[] { new KeyValuePair<string, string>("Content-Type", "application/json; charset=utf-8") };
        await WriteHttpResponseAsync(stream, statusCode, message, headers, body).ConfigureAwait(false);
    }

    private async Task HandleDeepLCoreAsync(Stream responseStream, byte[]? body)
    {
        try
        {
            using var ms = body != null ? new MemoryStream(body) : new MemoryStream();
            var (texts, targetLang, sourceLang) = await DeepLApiAdapter.ParseRequestAsync(ms, default);

            var translations = new List<object>();
            var src = sourceLang ?? "en";
            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text)) { translations.Add(new { detected_source_language = src, text = "" }); continue; }
                var translated = await _translationService.TranslateAsync(text, src, targetLang, default);
                translations.Add(new { detected_source_language = LanguageCodeHelper.ToDeepLFormat(src), text = translated });
            }

            var json = System.Text.Json.JsonSerializer.Serialize(new { translations });
            var buf = Encoding.UTF8.GetBytes(json);
            await WriteHttpResponseAsync(responseStream, 200, "OK",
                new[] { new KeyValuePair<string, string>("Content-Type", "application/json; charset=utf-8") },
                buf).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var err = System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message });
            var buf = Encoding.UTF8.GetBytes(err);
            await WriteHttpResponseAsync(responseStream, 500, "Internal Server Error",
                new[] { new KeyValuePair<string, string>("Content-Type", "application/json; charset=utf-8") },
                buf).ConfigureAwait(false);
        }
    }

    private async Task HandleGoogleCoreAsync(Stream responseStream, byte[]? body)
    {
        try
        {
            using var ms = body != null ? new MemoryStream(body) : new MemoryStream();
            var (contents, sourceLang, targetLang) = await GoogleApiAdapter.ParseRequestAsync(ms, default);

            var translations = new List<object>();
            var src = sourceLang ?? "en";
            foreach (var text in contents)
            {
                if (string.IsNullOrEmpty(text)) { translations.Add(new { translatedText = "" }); continue; }
                var translated = await _translationService.TranslateAsync(text, src, targetLang, default);
                translations.Add(new { translatedText = translated });
            }

            var json = System.Text.Json.JsonSerializer.Serialize(new { translations, glossaryTranslations = Array.Empty<object>() });
            var buf = Encoding.UTF8.GetBytes(json);
            await WriteHttpResponseAsync(responseStream, 200, "OK",
                new[] { new KeyValuePair<string, string>("Content-Type", "application/json; charset=utf-8") },
                buf).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var err = System.Text.Json.JsonSerializer.Serialize(new { error = new { message = ex.Message } });
            var buf = Encoding.UTF8.GetBytes(err);
            await WriteHttpResponseAsync(responseStream, 500, "Internal Server Error",
                new[] { new KeyValuePair<string, string>("Content-Type", "application/json; charset=utf-8") },
                buf).ConfigureAwait(false);
        }
    }

    private async Task HandleGoogleLegacySingleAsync(Stream responseStream, string path)
    {
        try
        {
            var query = path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : "";
            var qs = ParseQueryString(query);
            var q = qs.TryGetValue("q", out var qv) ? qv : "";
            var sl = qs.TryGetValue("sl", out var slv) ? slv : "auto";
            var tl = qs.TryGetValue("tl", out var tlv) ? tlv : "en";

            if (string.IsNullOrEmpty(q))
            {
                await WriteHttpErrorAsync(responseStream, 400, "Missing 'q' parameter").ConfigureAwait(false);
                return;
            }

            var src = sl == "auto" ? "en" : sl;
            var translated = await _translationService.TranslateAsync(q, src, tl, default).ConfigureAwait(false);
            var json = BuildGoogleLegacyResponse(translated, q, src);
            var buf = Encoding.UTF8.GetBytes(json);
            await WriteHttpResponseAsync(responseStream, 200, "OK",
                new[] { new KeyValuePair<string, string>("Content-Type", "application/json; charset=utf-8") },
                buf).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var err = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
            var buf = Encoding.UTF8.GetBytes(err);
            await WriteHttpResponseAsync(responseStream, 500, "Internal Server Error",
                new[] { new KeyValuePair<string, string>("Content-Type", "application/json; charset=utf-8") },
                buf).ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx >= 0)
            {
                var key = Uri.UnescapeDataString(pair[..idx].Replace('+', ' '));
                var value = Uri.UnescapeDataString(pair[(idx + 1)..].Replace('+', ' '));
                dict[key] = value;
            }
        }
        return dict;
    }

    private static string BuildGoogleLegacyResponse(string translated, string original, string sourceLang)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(translated);
        var origEscaped = System.Text.Json.JsonSerializer.Serialize(original);
        return $"[[[{escaped},{origEscaped},null,null,3]],null,\"{sourceLang}\"]";
    }
}
