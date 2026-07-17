using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
    public DbSet<LocalOperation> LocalOperations => Set<LocalOperation>();
    public DbSet<UserLocalOperation> UserLocalOperations => Set<UserLocalOperation>();
    public DbSet<OperationIntegration> OperationIntegrations => Set<OperationIntegration>();
    public DbSet<SupplyVendor> SupplyVendors => Set<SupplyVendor>();
    public DbSet<PayableInvoice> PayableInvoices => Set<PayableInvoice>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<VendorProduct> VendorProducts => Set<VendorProduct>();
    public DbSet<VendorPrice> VendorPrices => Set<VendorPrice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<PriceImportDocument> PriceImportDocuments => Set<PriceImportDocument>();
    public DbSet<PriceImportRow> PriceImportRows => Set<PriceImportRow>();
    public DbSet<PriceApprovalRule> PriceApprovalRules => Set<PriceApprovalRule>();
    public DbSet<CatalogSyncJob> CatalogSyncJobs => Set<CatalogSyncJob>();
    public DbSet<QuoteCase> QuoteCases => Set<QuoteCase>();
    public DbSet<QuoteVersion> QuoteVersions => Set<QuoteVersion>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();
    public DbSet<QuotePricingRule> QuotePricingRules => Set<QuotePricingRule>();
    public DbSet<QuoteAuditEvent> QuoteAuditEvents => Set<QuoteAuditEvent>();
    public DbSet<QuoteProcessingJob> QuoteProcessingJobs => Set<QuoteProcessingJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<NotificationRecipient>(entity =>
        {
            entity.Property(recipient => recipient.DisplayName)
                .HasMaxLength(120);

            entity.Property(recipient => recipient.EmailAddress)
                .HasMaxLength(256);

            entity.Property(recipient => recipient.CellPhoneNumber)
                .HasMaxLength(32);
        });

        builder.Entity<LocalOperation>(entity =>
        {
            entity.HasIndex(operation => operation.Slug).IsUnique();
            entity.HasIndex(operation => operation.HousecallProLocationId)
                .IsUnique()
                .HasFilter("\"HousecallProLocationId\" IS NOT NULL");
        });

        builder.Entity<UserLocalOperation>(entity =>
        {
            entity.HasKey(membership => new { membership.UserId, membership.LocalOperationId });
            entity.HasOne(membership => membership.User).WithMany(user => user.LocalOperationMemberships).HasForeignKey(membership => membership.UserId);
            entity.HasOne(membership => membership.LocalOperation).WithMany(operation => operation.UserMemberships).HasForeignKey(membership => membership.LocalOperationId);
        });

        builder.Entity<OperationIntegration>(entity =>
        {
            entity.HasIndex(integration => new { integration.LocalOperationId, integration.Provider }).IsUnique();
        });

        builder.Entity<SupplyVendor>(entity =>
        {
            entity.HasIndex(vendor => vendor.Name);
        });

        builder.Entity<PayableInvoice>(entity =>
        {
            entity.HasIndex(invoice => new { invoice.SupplyVendorId, invoice.InvoiceNumber }).IsUnique();
            entity.HasIndex(invoice => new { invoice.LocalOperationId, invoice.JobNumber });
            entity.HasOne(invoice => invoice.SupplyVendor).WithMany(vendor => vendor.PayableInvoices).HasForeignKey(invoice => invoice.SupplyVendorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(invoice => invoice.LocalOperation).WithMany().HasForeignKey(invoice => invoice.LocalOperationId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Product>(entity =>
        {
            entity.HasIndex(product => new { product.Manufacturer, product.ManufacturerPartNumber });
        });

        builder.Entity<VendorProduct>(entity =>
        {
            entity.HasIndex(item => new { item.SupplyVendorId, item.VendorSku }).IsUnique();
            entity.Property(item => item.PackageQuantity).HasPrecision(18, 4);
            entity.HasOne(item => item.SupplyVendor).WithMany(vendor => vendor.VendorProducts).HasForeignKey(item => item.SupplyVendorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Product).WithMany(product => product.VendorProducts).HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VendorPrice>(entity =>
        {
            entity.HasIndex(price => new { price.VendorProductId, price.EffectiveDate }).IsUnique();
            entity.HasOne(price => price.VendorProduct).WithMany(item => item.Prices).HasForeignKey(price => price.VendorProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InvoiceLine>(entity =>
        {
            entity.HasOne(line => line.PayableInvoice).WithMany(invoice => invoice.Lines).HasForeignKey(line => line.PayableInvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(line => line.VendorProduct).WithMany(item => item.InvoiceLines).HasForeignKey(line => line.VendorProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PriceImportDocument>(entity =>
        {
            entity.HasIndex(document => document.Sha256).IsUnique();
            entity.HasOne(document => document.SupplyVendor).WithMany(vendor => vendor.PriceImportDocuments).HasForeignKey(document => document.SupplyVendorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PriceImportRow>(entity =>
        {
            entity.HasOne(row => row.PriceImportDocument).WithMany(document => document.Rows).HasForeignKey(row => row.PriceImportDocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(row => row.VendorProduct).WithMany().HasForeignKey(row => row.VendorProductId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PriceApprovalRule>(entity => entity.HasIndex(rule => rule.SupplyVendorId).IsUnique());
        builder.Entity<CatalogSyncJob>(entity => entity.HasOne(job => job.SupplyVendor).WithMany(vendor => vendor.CatalogSyncJobs).HasForeignKey(job => job.SupplyVendorId).OnDelete(DeleteBehavior.Restrict));

        builder.Entity<QuoteCase>(entity =>
        {
            entity.HasIndex(quote => new { quote.LocalOperationId, quote.Status });
            entity.HasIndex(quote => quote.HousecallProQuoteId);
            entity.HasOne(quote => quote.LocalOperation).WithMany().HasForeignKey(quote => quote.LocalOperationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(quote => quote.AssignedUser).WithMany().HasForeignKey(quote => quote.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<QuoteVersion>(entity => entity.HasIndex(version => new { version.QuoteCaseId, version.VersionNumber }).IsUnique());
        builder.Entity<QuoteLine>(entity => entity.HasOne(line => line.QuoteVersion).WithMany(version => version.Lines).HasForeignKey(line => line.QuoteVersionId).OnDelete(DeleteBehavior.Cascade));
        builder.Entity<QuotePricingRule>(entity => entity.HasIndex(rule => rule.LocalOperationId).IsUnique());
        builder.Entity<QuoteAuditEvent>(entity => entity.HasOne(item => item.QuoteCase).WithMany(quote => quote.AuditEvents).HasForeignKey(item => item.QuoteCaseId).OnDelete(DeleteBehavior.Cascade));
        builder.Entity<QuoteProcessingJob>(entity => entity.HasOne(job => job.QuoteCase).WithMany(quote => quote.ProcessingJobs).HasForeignKey(job => job.QuoteCaseId).OnDelete(DeleteBehavior.Cascade));
    }
}
