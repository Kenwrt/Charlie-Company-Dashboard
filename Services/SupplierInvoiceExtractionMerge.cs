using System.Text.Json.Nodes;

namespace CharleyCompany.Dashboard.Web.Services;

public static class SupplierInvoiceExtractionMerge
{
    public static readonly string[] ExtractedFields = ["invoiceNumber", "invoiceDate", "dueDate", "jobNumber", "yourReference",
        "deliveryAddress", "currency", "isCreditNote", "tax", "freight", "discount", "total", "amountPaid", "paymentStateVerified", "lines"];

    public static JsonObject Merge(JsonObject current, JsonObject original, JsonObject incoming, out List<string> preserved)
    {
        var result = (JsonObject)current.DeepClone();
        var baseline = current["_extractionBaseline"] as JsonObject ?? original;
        preserved = [];
        foreach (var name in ExtractedFields)
        {
            // A changed line collection is preserved as a whole; never overwrite
            // a reviewer's corrected SKU, amount, added line or removed line.
            if (JsonNode.DeepEquals(current[name], baseline[name])) result[name] = incoming[name]?.DeepClone();
            else preserved.Add(name);
        }
        foreach (var name in new[] { "extractionVersion", "extractionEvidence", "extractionComplete" })
            result[name] = incoming[name]?.DeepClone();
        var oldWarnings = (baseline["warnings"] as JsonArray ?? []).Select(x => x?.GetValue<string>() ?? "").ToHashSet();
        var warnings = (incoming["warnings"] as JsonArray ?? []).Select(x => x?.GetValue<string>() ?? "")
            .Concat((current["warnings"] as JsonArray ?? []).Select(x => x?.GetValue<string>() ?? "").Where(x => !oldWarnings.Contains(x)))
            .Where(x => x.Length > 0).Distinct();
        result["warnings"] = new JsonArray(warnings.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        result["_extractionBaseline"] = incoming.DeepClone();
        return result;
    }
}
