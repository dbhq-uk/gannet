using System.Net;
using System.Net.Sockets;
using Gannet.Core.Services;
using Gannet.Providers.Http;

namespace Gannet.Providers.Tests.Http;

public class PublicAddressGuardTests
{
    // Both sides of every blocked range. The third column is whether the address is public.
    [Theory]
    [InlineData("0.0.0.0", false)]
    [InlineData("0.255.255.255", false)]
    [InlineData("1.0.0.0", true)]
    [InlineData("9.255.255.255", true)]
    [InlineData("10.0.0.0", false)]
    [InlineData("10.255.255.255", false)]
    [InlineData("11.0.0.0", true)]
    [InlineData("100.63.255.255", true)]
    [InlineData("100.64.0.0", false)]
    [InlineData("100.127.255.255", false)]
    [InlineData("100.128.0.0", true)]
    [InlineData("126.255.255.255", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.255.255.255", false)]
    [InlineData("128.0.0.0", true)]
    [InlineData("169.253.255.255", true)]
    [InlineData("169.254.0.0", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("169.254.255.255", false)]
    [InlineData("169.255.0.0", true)]
    [InlineData("172.15.255.255", true)]
    [InlineData("172.16.0.0", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.0", true)]
    [InlineData("191.255.255.255", true)]
    [InlineData("192.0.0.0", false)]
    [InlineData("192.0.0.255", false)]
    [InlineData("192.0.1.0", true)]
    [InlineData("192.0.1.255", true)]
    [InlineData("192.0.2.0", false)]
    [InlineData("192.0.2.255", false)]
    [InlineData("192.0.3.0", true)]
    [InlineData("192.167.255.255", true)]
    [InlineData("192.168.0.0", false)]
    [InlineData("192.168.255.255", false)]
    [InlineData("192.169.0.0", true)]
    [InlineData("198.17.255.255", true)]
    [InlineData("198.18.0.0", false)]
    [InlineData("198.19.255.255", false)]
    [InlineData("198.20.0.0", true)]
    [InlineData("198.51.99.255", true)]
    [InlineData("198.51.100.0", false)]
    [InlineData("198.51.100.255", false)]
    [InlineData("198.51.101.0", true)]
    [InlineData("203.0.112.255", true)]
    [InlineData("203.0.113.0", false)]
    [InlineData("203.0.113.255", false)]
    [InlineData("203.0.114.0", true)]
    [InlineData("223.255.255.255", true)]
    [InlineData("224.0.0.0", false)]
    [InlineData("239.255.255.255", false)]
    [InlineData("240.0.0.0", false)]
    [InlineData("255.255.255.254", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("1.1.1.1", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("fbff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", true)]
    [InlineData("fc00::", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false)]
    [InlineData("fe00::", true)]
    [InlineData("fe7f:ffff:ffff:ffff:ffff:ffff:ffff:ffff", true)]
    [InlineData("fe80::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false)]
    [InlineData("ff00::", false)]
    [InlineData("ff02::1", false)]
    [InlineData("2001:db7:ffff:ffff:ffff:ffff:ffff:ffff", true)]
    [InlineData("2001:db8::", false)]
    [InlineData("2001:db8:ffff:ffff:ffff:ffff:ffff:ffff", false)]
    [InlineData("2001:db9::", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("2000:ffff::1", true)]
    [InlineData("2001::", false)]
    [InlineData("2001:0:ffff:ffff:ffff:ffff:ffff:ffff", false)]
    [InlineData("2001:1::1", true)]
    [InlineData("febf::", false)]
    [InlineData("fec0::", false)]
    [InlineData("feff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false)]
    [InlineData("64:ff9b:1::", false)]
    [InlineData("64:ff9b:1:ffff:ffff:ffff:ffff:ffff", false)]
    [InlineData("64:ff9b:2::", true)]
    [InlineData("100::", false)]
    [InlineData("100::ffff:ffff:ffff:ffff", false)]
    [InlineData("100:0:0:1::", true)]
    [InlineData("101::", true)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("2002:a00:1::1", false)]
    [InlineData("2002:a9fe:a9fe::", false)]
    [InlineData("2002:c0a8:101::", false)]
    [InlineData("2002:808:808::", true)]
    [InlineData("2002:808:808:1:2:3:4:5", true)]
    [InlineData("2003::", true)]
    public void Ranges_are_judged_on_both_sides_of_each_boundary(string address, bool expected) =>
        Assert.Equal(expected, PublicAddressGuard.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:10.1.2.3", false)]
    [InlineData("::ffff:169.254.169.254", false)]
    [InlineData("::ffff:0.0.0.0", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]
    [InlineData("64:ff9b::808:808", true)]
    public void Mapped_and_nat64_forms_are_judged_by_the_embedded_ipv4_address(string address, bool expected) =>
        Assert.Equal(expected, PublicAddressGuard.IsPublic(IPAddress.Parse(address)));

    private static Func<string, CancellationToken, Task<IPAddress[]>> NoLookup { get; } =
        (host, _) => throw new InvalidOperationException($"DNS must not be asked about {host}");

    private static Func<string, CancellationToken, Task<IPAddress[]>> Resolves(params string[] addresses) =>
        (_, _) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST:8080/x")]
    [InlineData("http://localhost./")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://db.internal/")]
    [InlineData("https://a.b.internal./")]
    public async Task Local_names_are_refused_without_a_lookup(string url)
    {
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri(url), NoLookup, CancellationToken.None));
        Assert.Contains("is not a public address", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1:9000/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[64:ff9b::7f00:1]/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://0/")]
    public async Task Private_ip_literals_are_refused_without_a_lookup_and_the_message_has_no_ip(string url)
    {
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri(url), NoLookup, CancellationToken.None));
        Assert.Contains("not a public address", error.Message, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d+\.\d+|::|[0-9a-f]{2,}:", error.Message);
    }

    [Theory]
    [InlineData("http://8.8.8.8/")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    public async Task Public_ip_literals_pass_without_a_lookup(string url) =>
        await PublicAddressGuard.EnsurePublicAsync(new Uri(url), NoLookup, CancellationToken.None);

    [Theory]
    [InlineData("ftp://example.org/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.org/")]
    [InlineData("ws://example.org/")]
    public async Task Only_http_and_https_pass(string url) =>
        await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri(url), Resolves("8.8.8.8"), CancellationToken.None));

    [Theory]
    [InlineData("https://user@example.org/")]
    [InlineData("https://user:secret@example.org/")]
    public async Task Userinfo_is_refused_and_not_echoed(string url)
    {
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri(url), Resolves("8.8.8.8"), CancellationToken.None));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("user", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://ｌｏｃａｌｈｏｓｔ/")]
    [InlineData("http://ＬＯＣＡＬＨＯＳＴ:8080/")]
    [InlineData("http://app.ｌｏｃａｌｈｏｓｔ/")]
    [InlineData("http://ｄｂ.ｉｎｔｅｒｎａｌ/")]
    public async Task Full_width_local_names_are_refused_without_a_lookup(string url) =>
        await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri(url), NoLookup, CancellationToken.None));

    [Fact]
    public async Task The_lookup_is_given_the_ascii_form_of_an_international_name()
    {
        string? asked = null;
        await PublicAddressGuard.EnsurePublicAsync(new Uri("https://bücher.example/"), (host, _) =>
        {
            asked = host;
            return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
        }, CancellationToken.None);
        Assert.Equal("xn--bcher-kva.example", asked);
    }

    [Theory]
    [InlineData("203.0.114.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("example.org")]
    public void An_unreachable_message_never_carries_an_ip(string host)
    {
        var message = PublicAddressGuard.Unreachable(host).Message;
        Assert.DoesNotMatch(@"\d+\.\d+\.\d+|[0-9a-f]{3,}:", message);
        if (!IPAddress.TryParse(host, out _))
        {
            Assert.Contains(host, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_name_that_resolves_to_a_public_address_passes() =>
        await PublicAddressGuard.EnsurePublicAsync(new Uri("https://example.org/"), Resolves("93.184.216.34", "2606:2800:21f:cb07:6820:80da:af6b:8b2c"), CancellationToken.None);

    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.0.9")]
    public async Task A_name_that_resolves_to_a_private_address_is_refused_and_the_ip_is_not_in_the_message(string resolved)
    {
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri("https://rebind.example/"), Resolves(resolved), CancellationToken.None));
        Assert.Equal("rebind.example is not a public address", error.Message);
        Assert.DoesNotContain(resolved, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_private_address_among_public_ones_is_enough_to_refuse() =>
        await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri("https://mixed.example/"), Resolves("8.8.8.8", "10.0.0.1"), CancellationToken.None));

    [Fact]
    public async Task A_name_with_no_addresses_is_refused() =>
        await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri("https://nothing.example/"), Resolves(), CancellationToken.None));

    [Fact]
    public async Task A_failed_lookup_is_a_typed_failure()
    {
        Func<string, CancellationToken, Task<IPAddress[]>> fails = (_, _) => throw new SocketException((int)SocketError.HostNotFound);
        await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri("https://nowhere.example/"), fails, CancellationToken.None));
    }

    [Fact]
    public async Task The_public_overload_uses_real_dns_for_a_literal_without_a_lookup() =>
        await Assert.ThrowsAsync<ServiceFailedException>(() => PublicAddressGuard.EnsurePublicAsync(new Uri("http://127.0.0.1/"), CancellationToken.None));

    // The connect callback is the rebinding defence: it connects only to an address it has judged itself.
    [Fact]
    public async Task The_connect_callback_refuses_loopback_and_the_listener_sees_no_connection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();

        using var handler = new SocketsHttpHandler { ConnectCallback = PublicAddressGuard.ConnectAsync };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync($"http://127.0.0.1:{port}/"));

        await Task.Delay(300);
        Assert.False(accepted.IsCompleted, "the listener saw a connection");
        listener.Stop();
        await Assert.ThrowsAnyAsync<Exception>(() => accepted);
    }

    [Fact]
    public async Task The_connect_callback_refuses_a_name_that_resolves_to_loopback()
    {
        using var handler = new SocketsHttpHandler { ConnectCallback = PublicAddressGuard.ConnectAsync };
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync("http://localhost:9/"));
        Assert.IsType<ServiceFailedException>(error.InnerException);
    }
}
