using System.Diagnostics;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using Pingy.Core.Models;
using Pingy.Core.Util;

namespace Pingy.Core.Probing;

// L7 health check: once a TCP port is open, issue an HTTP(S) GET and assert the
// status code. Two HttpClients are constructed once and reused for the app lifetime
// — a per-request client would leak ephemeral ports and eventually fail to connect.
// _lax skips certificate validation for the AcceptSelfSigned case (intranet certs).
public sealed class HttpServiceCheck : IServiceCheck, IDisposable
{
    private readonly HttpClient _strict;
    private readonly HttpClient _lax;

    public HttpServiceCheck()
    {
        _strict = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = false,
        })
        { Timeout = Timeout.InfiniteTimeSpan };

        _lax = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        })
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<ServiceCheckResult> CheckAsync(Target target, TargetPort port, TimeSpan timeout, CancellationToken ct = default)
    {
        var check = port.Check;
        if (check is null)
            return new ServiceCheckResult(false, null, "NoCheck");

        var host = HostNormalizer.Normalize(target.Host);
        if (string.IsNullOrEmpty(host))
            return new ServiceCheckResult(false, null, "InvalidHost");

        var isHttps = string.Equals(check.Kind, "https", StringComparison.OrdinalIgnoreCase);
        var scheme = isHttps ? "https" : "http";
        var path = string.IsNullOrWhiteSpace(check.Path) ? "/" : check.Path!;
        if (!path.StartsWith('/')) path = "/" + path;
        var expected = check.ExpectStatus ?? 200;

        if (!Uri.TryCreate($"{scheme}://{host}:{port.Number}{path}", UriKind.Absolute, out var uri))
            return new ServiceCheckResult(false, null, "InvalidUrl");

        var client = (isHttps && check.AcceptSelfSigned) ? _lax : _strict;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            sw.Stop();
            var code = (int)resp.StatusCode;
            return new ServiceCheckResult(code == expected, sw.Elapsed.TotalMilliseconds, code.ToString());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ServiceCheckResult(false, null, "Timeout");
        }
        catch (HttpRequestException ex) when (HasCertFailure(ex))
        {
            return new ServiceCheckResult(false, null, "CertInvalid");
        }
        catch (HttpRequestException)
        {
            return new ServiceCheckResult(false, null, "HttpError");
        }
        catch (Exception ex)
        {
            return new ServiceCheckResult(false, null, ex.GetType().Name);
        }
    }

    private static bool HasCertFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is AuthenticationException) return true;
        }
        return false;
    }

    public void Dispose()
    {
        _strict.Dispose();
        _lax.Dispose();
    }
}
