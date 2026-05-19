using System.Net;
using System.Net.Sockets;
using Pingy.Core.Models;
using Pingy.Core.Probing;

namespace Pingy.Core.Tests;

public class TcpPortProbeTests
{
    private static Target Loopback(string id = "t1") =>
        new(Id: id, Host: "127.0.0.1", Kind: "host");

    [Fact]
    public async Task Connects_to_listening_loopback_port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new TcpPortProbe();

            var result = await probe.ProbeAsync(Loopback(), port, TimeSpan.FromSeconds(2));

            Assert.True(result.Success, $"Expected Connected, got {result.Status}");
            Assert.Equal("Connected", result.Status);
            Assert.Equal(port, result.Port);
            Assert.NotNull(result.RttMs);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Refused_on_closed_loopback_port()
    {
        // Grab a port, then immediately release it — high chance nothing else is listening on it.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var probe = new TcpPortProbe();
        var result = await probe.ProbeAsync(Loopback(), port, TimeSpan.FromSeconds(2));

        Assert.False(result.Success);
        // On Windows loopback a closed port reliably yields ConnectionRefused; accept Timeout too
        // in case the test host treats it differently (defensive — we only care that we DON'T claim Connected).
        Assert.Contains(result.Status, new[] { "Refused", "Timeout" });
    }

    [Fact]
    public async Task Rejects_invalid_port_without_socket_call()
    {
        var probe = new TcpPortProbe();
        var result = await probe.ProbeAsync(Loopback(), 0, TimeSpan.FromSeconds(2));

        Assert.False(result.Success);
        Assert.Equal("InvalidPort", result.Status);
    }
}
