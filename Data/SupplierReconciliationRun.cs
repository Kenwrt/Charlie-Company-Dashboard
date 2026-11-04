using System.ComponentModel.DataAnnotations;
namespace CharleyCompany.Dashboard.Web.Data;
public sealed class SupplierReconciliationRun
{
    public int Id { get; set; }
    [StringLength(64)] public string SnapshotHash { get; set; } = "";
    public int SupplyVendorId { get; set; }
    public int LocalOperationId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SourceJson { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
