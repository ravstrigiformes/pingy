using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Pingy.Core.Models;
using Pingy.Core.Probing;

namespace Pingy.Core.Tests;

// HTTP cases use HttpListener bound to http://localhost:<port>/ — the "localhost"
// prefix is exempt from the Windows urlacl/admin requirement.
// HTTPS cases can't use HttpListener (needs a netsh sslcert binding), so they run a
// minimal TcpListener + SslStream responder with a BCL-generated self-signed cert.
public class HttpServiceCheckTests
{
    private static int FreeLoopbackPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static (Target, TargetPort) Make(int port, ServiceCheck check) =>
        (new Target("t1", "localhost", "host"), new TargetPort(port, null, check));

    private static HttpListener StartHttpServer(int port, int statusCode, string okPath = "/", int delayMs = 0)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                if (delayMs > 0) await Task.Delay(delayMs);
                try
                {
                    ctx.Response.StatusCode =
                        (okPath != "/" && ctx.Request.Url?.AbsolutePath != okPath) ? 404 : statusCode;
                    ctx.Response.Close();
                }
                catch { /* client may have already gone away */ }
            }
        });
        return listener;
    }

    [Fact]
    public async Task Ok_when_status_matches()
    {
        var port = FreeLoopbackPort();
        using var server = StartHttpServer(port, 200);
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("http"));

        var r = await check.CheckAsync(t, p, TimeSpan.FromSeconds(3));

        Assert.True(r.Ok, $"expected Ok, got {r.Status}");
        Assert.Equal("200", r.Status);
        Assert.NotNull(r.RttMs);
    }

    [Fact]
    public async Task Degraded_when_status_500()
    {
        var port = FreeLoopbackPort();
        using var server = StartHttpServer(port, 500);
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("http"));

        var r = await check.CheckAsync(t, p, TimeSpan.FromSeconds(3));

        Assert.False(r.Ok);
        Assert.Equal("500", r.Status);
    }

    [Fact]
    public async Task Degraded_when_expect_mismatch()
    {
        var port = FreeLoopbackPort();
        using var server = StartHttpServer(port, 200);
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("http", ExpectStatus: 204));

        var r = await check.CheckAsync(t, p, TimeSpan.FromSeconds(3));

        Assert.False(r.Ok);
        Assert.Equal("200", r.Status);
    }

    [Fact]
    public async Task Timeout_when_server_hangs()
    {
        var port = FreeLoopbackPort();
        using var server = StartHttpServer(port, 200, delayMs: 3000);
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("http"));

        var r = await check.CheckAsync(t, p, TimeSpan.FromMilliseconds(400));

        Assert.False(r.Ok);
        Assert.Equal("Timeout", r.Status);
    }

    [Fact]
    public async Task CustomPath_routes_correctly()
    {
        var port = FreeLoopbackPort();
        using var server = StartHttpServer(port, 200, okPath: "/healthz");
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("http", Path: "/healthz"));

        var r = await check.CheckAsync(t, p, TimeSpan.FromSeconds(3));

        Assert.True(r.Ok, $"expected Ok, got {r.Status}");
    }

    [Fact]
    public async Task Not_ok_when_nothing_listening()
    {
        // In production the L7 layer only runs after TCP connect succeeds, so this path
        // is defensive. The exact failure label is environment-dependent (HttpError if the
        // socket is refused fast, Timeout if the connect stalls) — we only assert not-Ok.
        var port = FreeLoopbackPort();
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("http"));

        var r = await check.CheckAsync(t, p, TimeSpan.FromSeconds(2));

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Https_self_signed_accepted_when_insecure()
    {
        using var cert = SelfSignedCert();
        var (serverTask, port) = StartHttpsServer(cert);
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("https", AcceptSelfSigned: true));

        var r = await check.CheckAsync(t, p, TimeSpan.FromSeconds(5));
        await serverTask;

        Assert.True(r.Ok, $"expected Ok, got {r.Status}");
        Assert.Equal("200", r.Status);
    }

    [Fact]
    public async Task Https_self_signed_rejected_without_insecure()
    {
        using var cert = SelfSignedCert();
        var (serverTask, port) = StartHttpsServer(cert);
        using var check = new HttpServiceCheck();
        var (t, p) = Make(port, new ServiceCheck("https", AcceptSelfSigned: false));

        var r = await check.CheckAsync(t, p, TimeSpan.FromSeconds(5));
        await serverTask;

        Assert.False(r.Ok);
        Assert.Equal("CertInvalid", r.Status);
    }

    private static X509Certificate2 SelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Re-import via PFX so the private key is usable by SslStream on Windows.
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    // Single-shot HTTPS responder: accepts one TLS connection, replies 200, closes.
    private static (Task serverTask, int port) StartHttpsServer(X509Certificate2 cert)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var task = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var ssl = new SslStream(client.GetStream(), false);
                await ssl.AuthenticateAsServerAsync(cert, clientCertificateRequired: false,
                    checkCertificateRevocation: false);
                var buf = new byte[4096];
                await ssl.ReadAsync(buf);
                var resp = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await ssl.WriteAsync(resp);
                await ssl.FlushAsync();
            }
            catch
            {
                // The strict-validation client aborts the handshake — expected for the reject test.
            }
            finally
            {
                listener.Stop();
            }
        });

        return (task, port);
    }
}
