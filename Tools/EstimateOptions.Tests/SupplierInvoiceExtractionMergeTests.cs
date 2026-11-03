using System.Text.Json.Nodes;
using CharleyCompany.Dashboard.Web.Services;
using Xunit;

public sealed class SupplierInvoiceExtractionMergeTests
{
    [Fact]
    public void RefreshPreservesManualTaxLinesPaymentAndLocation()
    {
        var original = JsonNode.Parse("""{"tax":0,"total":0,"lines":[{"sku":"Each"}],"amountPaid":0,"warnings":["old parser warning"]}""")!.AsObject();
        var current = (JsonObject)original.DeepClone();
        current["tax"] = 7.25m; current["amountPaid"] = 20m;
        current["lines"] = JsonNode.Parse("""[{"sku":"REVIEWED-SKU"}]""");
        current["localOperationId"] = 22; current["payingEntityCode"] = "ventures";
        var incoming = JsonNode.Parse("""{"tax":8,"total":108,"lines":[{"sku":"PRINTED-SKU"}],"amountPaid":0,"warnings":["new parser warning"],"extractionVersion":"v1"}""")!.AsObject();
        var merged = SupplierInvoiceExtractionMerge.Merge(current, original, incoming, out var preserved);
        Assert.Equal(7.25m, merged["tax"]!.GetValue<decimal>());
        Assert.Equal(108, merged["total"]!.GetValue<int>());
        Assert.Equal(20m, merged["amountPaid"]!.GetValue<decimal>());
        Assert.Equal("REVIEWED-SKU", merged["lines"]![0]!["sku"]!.GetValue<string>());
        Assert.Equal(22, merged["localOperationId"]!.GetValue<int>());
        Assert.Contains("tax", preserved); Assert.Contains("lines", preserved);
        Assert.DoesNotContain("old parser warning", merged["warnings"]!.ToJsonString());
    }

    [Fact]
    public void NextRevisionCanRefreshPriorAutomatedValues()
    {
        var original = JsonNode.Parse("""{"total":0,"lines":[],"warnings":[]}""")!.AsObject();
        var first = JsonNode.Parse("""{"total":100,"lines":[{"sku":"A"}],"warnings":[],"extractionVersion":"v1"}""")!.AsObject();
        var merged = SupplierInvoiceExtractionMerge.Merge(original, original, first, out _);
        var next = JsonNode.Parse("""{"total":120,"lines":[{"sku":"B"}],"warnings":[],"extractionVersion":"v2"}""")!.AsObject();
        var updated = SupplierInvoiceExtractionMerge.Merge(merged, original, next, out var preserved);
        Assert.Equal(120, updated["total"]!.GetValue<int>());
        Assert.Equal("B", updated["lines"]![0]!["sku"]!.GetValue<string>());
        Assert.Empty(preserved);
        Assert.Equal(0, original["total"]!.GetValue<int>());
    }
}
