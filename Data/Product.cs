using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class Product
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [StringLength(100)] public string? Category { get; set; }
    [StringLength(120)] public string? Manufacturer { get; set; }
    [StringLength(100)] public string? ManufacturerPartNumber { get; set; }
    [Required, StringLength(40)] public string UnitOfMeasure { get; set; } = "Each";
    public bool IsActive { get; set; } = true;
    public ICollection<VendorProduct> VendorProducts { get; set; } = [];
}
