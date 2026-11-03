using System.Net;

namespace CharleyCompany.Dashboard.Web.Services;

public static class SupplierInvoiceTransport
{
    public static bool IsSecure(bool https, bool development, IPAddress? peer,
        string forwardedProtocol, IEnumerable<string> trustedProxies)
    {
        if (https) return true;
        if (peer is null) return false;
        peer = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
        if (development && IPAddress.IsLoopback(peer)) return true;
        return forwardedProtocol.Equals("https", StringComparison.OrdinalIgnoreCase)
            && trustedProxies.Any(value => IPAddress.TryParse(value, out var proxy)
                && peer.Equals(proxy.IsIPv4MappedToIPv6 ? proxy.MapToIPv4() : proxy));
    }
}
