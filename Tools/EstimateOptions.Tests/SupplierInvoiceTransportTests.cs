using System.Net;
using CharleyCompany.Dashboard.Web.Services;
using Xunit;

public sealed class SupplierInvoiceTransportTests
{
    [Fact]
    public void ForwardedTlsRequiresAnExactTrustedPeer()
    {
        var peer = IPAddress.Parse("192.0.2.10");
        Assert.False(SupplierInvoiceTransport.IsSecure(false, false, peer, "https", []));
        Assert.False(SupplierInvoiceTransport.IsSecure(false, false, peer, "https", ["192.0.2.11"]));
        Assert.True(SupplierInvoiceTransport.IsSecure(false, false, peer, "https", ["192.0.2.10"]));
        Assert.False(SupplierInvoiceTransport.IsSecure(false, false, peer, "http", ["192.0.2.10"]));
        Assert.False(SupplierInvoiceTransport.IsSecure(false, false, peer, "https,http", ["192.0.2.10"]));
    }

    [Fact]
    public void ProductionLoopbackIsNotImplicitlyTrusted() =>
        Assert.False(SupplierInvoiceTransport.IsSecure(false, false, IPAddress.Loopback, "https", []));
}
