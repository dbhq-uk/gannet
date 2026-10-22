using System.Net;
using System.Net.Sockets;
using Gannet.Core.Services;

namespace Gannet.Providers.Http;

/// <summary>
/// Keeps page fetches on the public internet. A page address comes from a search result or from the model, so it is not trusted: it must not
/// reach loopback, the local network or a cloud metadata service. Messages never carry an IP address.
/// </summary>
internal static class PublicAddressGuard
{
    private const string DefaultService = "Built-in reader";

    private static readonly (byte[] Prefix, int Bits)[] BlockedV4 =
    [
        ([0, 0, 0, 0], 8),
        ([10, 0, 0, 0], 8),
        ([100, 64, 0, 0], 10),
        ([127, 0, 0, 0], 8),
        ([169, 254, 0, 0], 16),
        ([172, 16, 0, 0], 12),
        ([192, 0, 0, 0], 24),
        ([192, 0, 2, 0], 24),
        ([192, 168, 0, 0], 16),
        ([198, 18, 0, 0], 15),
        ([198, 51, 100, 0], 24),
        ([203, 0, 113, 0], 24),
        ([224, 0, 0, 0], 4),
        ([240, 0, 0, 0], 4),
    ];

    public static bool IsPublic(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsPublicV4(address.GetAddressBytes());
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        // ::ffff:a.b.c.d, NAT64 64:ff9b::a.b.c.d and the old IPv4-compatible ::a.b.c.d (this also covers :: and ::1): the IPv4 address decides.
        if (address.IsIPv4MappedToIPv6 || IsNat64(bytes) || bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0)
        {
            return IsPublicV4(bytes[12..]);
        }

        // 6to4 (2002::/16) carries an IPv4 address in bits 16 to 47, and that address decides.
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return IsPublicV4(bytes.AsSpan(2, 4));
        }

        return (bytes[0] & 0xFE) != 0xFC                                                         // fc00::/7
            && !(bytes[0] == 0xFE && (bytes[1] & 0xC0) is 0x80 or 0xC0)                           // fe80::/10 and fec0::/10
            && bytes[0] != 0xFF                                                                  // ff00::/8
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)    // 2001:db8::/32
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)    // 2001::/32 (Teredo)
            && !(bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B && bytes[4] == 0x00 && bytes[5] == 0x01) // 64:ff9b:1::/48
            && !(bytes[0] == 0x01 && bytes.AsSpan(1, 7).IndexOfAnyExcept((byte)0) < 0);           // 100::/64
    }

    /// <summary>
    /// Throws <see cref="ServiceFailedException"/> unless the address is http or https, has no userinfo and resolves only to public addresses.
    /// The exception names <paramref name="service"/>, the reader that was asked to fetch the page.
    /// </summary>
    public static Task EnsurePublicAsync(Uri url, CancellationToken ct, string service = DefaultService) =>
        EnsurePublicAsync(url, Dns.GetHostAddressesAsync, ct, service);

    internal static async Task EnsurePublicAsync(Uri url, Func<string, CancellationToken, Task<IPAddress[]>> resolve, CancellationToken ct, string service = DefaultService)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ServiceFailedException(service, "Only http and https addresses can be read.");
        }

        if (url.UserInfo.Length > 0)
        {
            throw new ServiceFailedException(service, "Addresses that carry a login are not read.");
        }

        var host = url.IdnHost.TrimEnd('.');
        if (IPAddress.TryParse(host, out var literal))
        {
            if (!IsPublic(literal))
            {
                throw new ServiceFailedException(service, "The page address is not a public address.");
            }

            return;
        }

        if (host.Length == 0 || IsLocalName(host))
        {
            throw NotPublic(host, service);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await resolve(host, ct);
        }
        catch (SocketException)
        {
            throw new ServiceFailedException(service, $"{host} could not be found.");
        }

        if (addresses.Length == 0 || !addresses.All(IsPublic))
        {
            throw NotPublic(host, service);
        }
    }

    /// <summary>
    /// For <see cref="SocketsHttpHandler.ConnectCallback"/>. Resolves the name itself and connects only to an address that is public, so a name that
    /// answered "public" to an earlier check cannot be switched to a private address between the check and the connection.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = (await Dns.GetHostAddressesAsync(endPoint.Host, ct)).Where(IsPublic).ToArray();
        if (addresses.Length == 0)
        {
            throw NotPublic(endPoint.Host);
        }

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw Unreachable(endPoint.Host);
    }

    /// <summary>Never carries an IP address: for an IP-literal host the message does not name it.</summary>
    public static ServiceFailedException Unreachable(string host) =>
        new(DefaultService, IPAddress.TryParse(host, out _) ? "The page address could not be reached." : $"{host.TrimEnd('.')} could not be reached.");

    private static ServiceFailedException NotPublic(string host, string service = DefaultService) =>
        new(service, IPAddress.TryParse(host, out _) ? "The page address is not a public address." : $"{host.TrimEnd('.')} is not a public address");

    private static bool IsLocalName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsNat64(byte[] bytes) =>
        bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B && bytes.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0;

    private static bool IsPublicV4(ReadOnlySpan<byte> bytes)
    {
        var value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        foreach (var (prefix, bits) in BlockedV4)
        {
            var network = ((uint)prefix[0] << 24) | ((uint)prefix[1] << 16) | ((uint)prefix[2] << 8) | prefix[3];
            var mask = uint.MaxValue << (32 - bits);
            if ((value & mask) == network)
            {
                return false;
            }
        }

        return true;
    }
}
