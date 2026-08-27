using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class LocalOperation
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(120)] public string DisplayName { get; set; } = string.Empty;
    [NotMapped] public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    [Required, StringLength(80)] public string Slug { get; set; } = string.Empty;
    [Required, StringLength(100)] public string TimeZone { get; set; } = "America/Chicago";
    [StringLength(160)] public string? HousecallProLocationId { get; set; }
    [StringLength(160)] public string? LegalName { get; set; }
    [StringLength(160)] public string? AddressLine1 { get; set; }
    [StringLength(160)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(40)] public string? StateOrProvince { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(80)] public string Country { get; set; } = "United States";
    [StringLength(40)] public string? TaxIdentificationNumber { get; set; }
    [StringLength(80)] public string? TaxClassification { get; set; }
    [StringLength(80)] public string? SalesTaxAccountNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<UserLocalOperation> UserMemberships { get; set; } = [];
    public ICollection<OperationIntegration> Integrations { get; set; } = [];
}
