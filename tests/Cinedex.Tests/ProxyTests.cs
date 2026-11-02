using System.Net;
using System.Net.Sockets;
using System.Text;
using Cinedex.App.Services;
using Cinedex.Metadata.Tmdb;

namespace Cinedex.Tests;

/// <summary>Proxy réseau : saisie serveur:port, mémorisation, et passage effectif des requêtes par le proxy.</summary>
public sealed class ProxyTests
{
    [Theory]
    [InlineData("myserver.proxy.com:8080", false, "http://myserver.proxy.com:8080/")]
    [InlineData(" http://myserver.proxy.com:3128/ ", false, "http://myserver.proxy.com:3128/")]
    [InlineData("10.0.0.5:443", true, "https://10.0.0.5/")]
    public void Valid_addresses_are_read(string address, bool https, string expected)
    {
        Assert.True(ProxySettings.TryParse(address, https, out var uri));
        Assert.Equal(expected, uri!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("myserver.proxy.com")]
    [InlineData("myserver.proxy.com:")]
    [InlineData("myserver.proxy.com:99999")]
    [InlineData("user@myserver:8080")]
    [InlineData("myserver:80/chemin")]
    public void Invalid_addresses_are_refused(string address) => Assert.False(ProxySettings.TryParse(address, false, out _));

    [Fact]
    public void Proxy_is_off_by_default_and_remembered()
    {
        using var temp = new TempDirectory();
        Assert.False(ProxySettings.Load(temp.Path).Enabled);

        new ProxySettings { Enabled = true, UseHttps = true, Address = "proxy.local:8443" }.Save(temp.Path);
        var loaded = ProxySettings.Load(temp.Path);
        Assert.Equal((true, true, "https://proxy.local:8443/"), (loaded.Enabled, loaded.UseHttps, loaded.ProxyUri!.ToString()));
    }

    [Fact]
    public async Task Requests_go_through_the_configured_proxy_only_when_enabled()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var current = new ProxySettings { Enabled = true, Address = $"127.0.0.1:{port}" };
        var proxy = new SwitchableProxy(() => current);
        Assert.Equal(new Uri($"http://127.0.0.1:{port}/"), proxy.GetProxy(new Uri("https://api.themoviedb.org/3/")));

        // Le faux proxy lit la demande CONNECT puis refuse : la requête est bien passée par lui.
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
            await stream.WriteAsync("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
            return Encoding.ASCII.GetString(buffer, 0, read);
        }, TestContext.Current.CancellationToken);

        using var http = TmdbClient.CreateHttpClient(proxy);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync(new Uri("https://api.themoviedb.org/3/configuration"), TestContext.Current.CancellationToken));
        Assert.StartsWith("CONNECT api.themoviedb.org:443", await received, StringComparison.Ordinal);

        current = current with { Enabled = false };
        Assert.NotEqual(new Uri($"http://127.0.0.1:{port}/"), proxy.GetProxy(new Uri("https://api.themoviedb.org/3/")));
    }
}
