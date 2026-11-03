using System.Text.Json;
using CharleyCompany.Dashboard.Web.Models;
using Xunit;

public sealed class SupplierInvoicePrecisionTests
{
    [Fact]
    public void UncertainExtractedAmountsSurviveReviewWithoutSilentRounding()
    {
        var payload = new SupplierInvoicePayload
        {
            Total = 12.345m,
            Lines = [new() { Quantity = 1, UnitPrice = 12.345m, LineTotal = 12.345m }]
        };
        var restored = JsonSerializer.Deserialize<SupplierInvoicePayload>(JsonSerializer.Serialize(payload, SupplierInvoicePayload.JsonOptions), SupplierInvoicePayload.JsonOptions)!;
        Assert.Equal(12.345m, restored.Total);
        Assert.Equal(12.345m, restored.Lines[0].LineTotal);
        Assert.False(restored.HasSupportedPostingPrecision());
    }

    [Fact]
    public void FourDecimalUnitPriceAndCentsTotalCanBePosted()
    {
        var payload = new SupplierInvoicePayload
        {
            Total = 24.69m,
            Lines = [new() { Quantity = 2, UnitPrice = 12.345m, LineTotal = 24.69m }]
        };
        Assert.True(payload.HasSupportedPostingPrecision());
    }
}
