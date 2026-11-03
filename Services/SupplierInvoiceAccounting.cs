using CharleyCompany.Dashboard.Web.Models;
using CharleyCompany.Dashboard.Web.Options;

namespace CharleyCompany.Dashboard.Web.Services;

// Server-owned assignments. Collector input and browser fields cannot enable exports.
public static class SupplierInvoiceAccounting
{
    public const string Version = "entity-policy-v1";

    public static void Assign(SupplierInvoicePayload payload, string? locationSlug, SupplierInvoiceOptions settings)
    {
        var owner = "";
        if (locationSlug is not null)
        {
            // Nashville is outside Ventures accounting regardless of configured defaults.
            if (locationSlug.Equals("nashville", StringComparison.OrdinalIgnoreCase)) owner = "nashville";
            else settings.LocationEntityCodes.TryGetValue(locationSlug, out owner);
        }
        payload.PurchaseEntityCode = owner ?? "";
        var eligible = payload.PurchaseEntityCode.Length > 0
            && !payload.PurchaseEntityCode.Equals("nashville", StringComparison.OrdinalIgnoreCase)
            && settings.AccountingExportEntityCodes.Contains(payload.PurchaseEntityCode, StringComparer.OrdinalIgnoreCase);
        payload.AccountingPolicy = eligible ? "AccountingReview" : "ReportingOnly";
        payload.AccountingHandoffStatus = eligible ? "ReadyForAccountingReview" : "NotEligible";
        payload.AccountingPolicyVersion = Version;
    }
}
