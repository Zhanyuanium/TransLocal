using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using local_translate_provider.ApiAdapters;
using local_translate_provider.Models;
using local_translate_provider.Services;

namespace local_translate_provider.Services;

/// <summary>
/// HTTP server that exposes DeepL and Google Translate format endpoints.
/// </summary>
public sealed class HttpTranslationServer
{
    private readonly AppSettings _settings;
    private readonly TranslationService _translationService;
    private HttpListener? _listener;
    private Task? _runTask;
    private readonly object _lock = new();

    public HttpTranslationServer(AppSettings settings, TranslationService translationService)
    {
        _settings = settings;
        _translationService = translationService;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_listener != null) return;

            var port = _settings.Port;
            var prefix = $"http://localhost:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);

            _listener.Start();
            _runTask = RunAsync();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _listener?.Stop();
            _listener?.Close();
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
        while (_listener != null && _listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleRequestAsync(ctx);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        if (req.HttpMethod != "POST")
        {
            resp.StatusCode = 405;
            resp.Close();
            return;
        }

        if (!string.IsNullOrEmpty(_settings.ApiKey) &&
            req.Headers["Authorization"] != $"DeepL-Auth-Key {_settings.ApiKey}" &&
            req.Headers["Authorization"] != $"Bearer {_settings.ApiKey}")
        {
            resp.StatusCode = 401;
            resp.Close();
            return;
        }

        try
        {
            var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (_settings.EnableDeepLEndpoint && path.EndsWith("/v2/translate", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDeepLAsync(ctx).ConfigureAwait(false);
                return;
            }
            if (_settings.EnableGoogleEndpoint && path.Contains(":translateText", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGoogleAsync(ctx).ConfigureAwait(false);
                return;
            }

            resp.StatusCode = 404;
        }
        catch (Exception)
        {
            resp.StatusCode = 500;
        }
        finally
        {
            resp.Close();
        }
    }

    private async Task HandleDeepLAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        resp.ContentType = "application/json; charset=utf-8";

        try
        {
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(body));
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
            resp.ContentLength64 = buf.Length;
            await resp.OutputStream.WriteAsync(buf);
        }
        catch (Exception ex)
        {
            resp.StatusCode = 500;
            var err = System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message });
            var buf = Encoding.UTF8.GetBytes(err);
            resp.ContentLength64 = buf.Length;
            await resp.OutputStream.WriteAsync(buf);
        }
        finally
        {
            resp.OutputStream.Close();
        }
    }

    private async Task HandleGoogleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        resp.ContentType = "application/json; charset=utf-8";

        try
        {
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(body));
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
            resp.ContentLength64 = buf.Length;
            await resp.OutputStream.WriteAsync(buf);
        }
        catch (Exception ex)
        {
            resp.StatusCode = 500;
            var err = System.Text.Json.JsonSerializer.Serialize(new { error = new { message = ex.Message } });
            var buf = Encoding.UTF8.GetBytes(err);
            resp.ContentLength64 = buf.Length;
            await resp.OutputStream.WriteAsync(buf);
        }
        finally
        {
            resp.OutputStream.Close();
        }
    }
}
