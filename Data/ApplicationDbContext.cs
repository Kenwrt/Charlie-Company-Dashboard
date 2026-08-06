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
    public DbSet<QuoteProjectTask> QuoteProjectTasks => Set<QuoteProjectTask>();
    public DbSet<QuoteProjectTaskPhoto> QuoteProjectTaskPhotos => Set<QuoteProjectTaskPhoto>();
    public DbSet<QuoteTaskAnalysis> QuoteTaskAnalyses => Set<QuoteTaskAnalysis>();
    public DbSet<QuoteTaskAnalysisMaterial> QuoteTaskAnalysisMaterials => Set<QuoteTaskAnalysisMaterial>();
    public DbSet<QuoteTaskAnalysisReviewItem> QuoteTaskAnalysisReviewItems => Set<QuoteTaskAnalysisReviewItem>();
    public DbSet<QuotePricingRule> QuotePricingRules => Set<QuotePricingRule>();
    public DbSet<QuoteAuditEvent> QuoteAuditEvents => Set<QuoteAuditEvent>();
    public DbSet<QuoteProcessingJob> QuoteProcessingJobs => Set<QuoteProcessingJob>();
    public DbSet<OperationalEvent> OperationalEvents => Set<OperationalEvent>();
    public DbSet<HousecallProJob> HousecallProJobs => Set<HousecallProJob>();
    public DbSet<HousecallProEstimate> HousecallProEstimates => Set<HousecallProEstimate>();
    public DbSet<HousecallProEstimateCommunication> HousecallProEstimateCommunications => Set<HousecallProEstimateCommunication>();
    public DbSet<HousecallProEstimateFollowUp> HousecallProEstimateFollowUps => Set<HousecallProEstimateFollowUp>();
    public DbSet<HousecallProJobFollowUp> HousecallProJobFollowUps => Set<HousecallProJobFollowUp>();
    public DbSet<HousecallProJobProgress> HousecallProJobProgress => Set<HousecallProJobProgress>();
    public DbSet<HousecallProJobBlocker> HousecallProJobBlockers => Set<HousecallProJobBlocker>();
    public DbSet<HousecallProJobPaymentMilestone> HousecallProJobPaymentMilestones => Set<HousecallProJobPaymentMilestone>();
    public DbSet<HousecallProJobProgressEvent> HousecallProJobProgressEvents => Set<HousecallProJobProgressEvent>();
    public DbSet<CentComChatSession> CentComChatSessions => Set<CentComChatSession>();
    public DbSet<CentComChatMessage> CentComChatMessages => Set<CentComChatMessage>();
    public DbSet<CostingPolicyVersion> CostingPolicyVersions => Set<CostingPolicyVersion>();
    public DbSet<CostingPolicyRule> CostingPolicyRules => Set<CostingPolicyRule>();
    public DbSet<QuoteCostSnapshot> QuoteCostSnapshots => Set<QuoteCostSnapshot>();
    public DbSet<QuoteTaskCostSnapshot> QuoteTaskCostSnapshots => Set<QuoteTaskCostSnapshot>();
    public DbSet<MaterialExclusionRule> MaterialExclusionRules => Set<MaterialExclusionRule>();
    public DbSet<QuoteTaskAnalysisExclusion> QuoteTaskAnalysisExclusions => Set<QuoteTaskAnalysisExclusion>();
    public DbSet<StandardSupplyKit> StandardSupplyKits => Set<StandardSupplyKit>();
    public DbSet<StandardSupplyKitItem> StandardSupplyKitItems => Set<StandardSupplyKitItem>();
    public DbSet<CrewRateCard> CrewRateCards => Set<CrewRateCard>();
    public DbSet<TaskMarginRule> TaskMarginRules => Set<TaskMarginRule>();
    public DbSet<QuoteTaskSupplyCostSnapshot> QuoteTaskSupplyCostSnapshots => Set<QuoteTaskSupplyCostSnapshot>();

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
            entity.HasIndex(item => new { item.IsPreferred, item.PreferencePriority });
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
        builder.Entity<QuoteProjectTask>(entity =>
        {
            entity.HasIndex(task => new { task.QuoteCaseId, task.SortOrder }).IsUnique();
            entity.HasQueryFilter(task => !task.IsDeleted);
            entity.HasOne(task => task.QuoteCase).WithMany(quote => quote.ProjectTasks).HasForeignKey(task => task.QuoteCaseId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<HousecallProJobProgress>(entity =>
        {
            entity.HasIndex(x => x.HousecallProJobId).IsUnique();
            entity.HasOne(x => x.HousecallProJob).WithOne(x => x.Progress).HasForeignKey<HousecallProJobProgress>(x => x.HousecallProJobId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<HousecallProJobBlocker>(entity =>
        {
            entity.HasIndex(x => new { x.HousecallProJobId, x.ResolvedOn });
            entity.Property(x => x.RevenueAtRisk).HasPrecision(18, 2);
            entity.HasOne(x => x.HousecallProJob).WithMany(x => x.Blockers).HasForeignKey(x => x.HousecallProJobId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<HousecallProJobPaymentMilestone>(entity =>
        {
            entity.HasIndex(x => new { x.HousecallProJobId, x.Status });
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasOne(x => x.HousecallProJob).WithMany(x => x.PaymentMilestones).HasForeignKey(x => x.HousecallProJobId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<HousecallProJobProgressEvent>(entity =>
        {
            entity.HasIndex(x => new { x.HousecallProJobId, x.OccurredAt });
            entity.HasOne(x => x.HousecallProJob).WithMany(x => x.ProgressEvents).HasForeignKey(x => x.HousecallProJobId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<QuoteProjectTaskPhoto>(entity =>
        {
            entity.HasIndex(photo => new { photo.QuoteProjectTaskId, photo.CapturedAt });
            entity.HasOne(photo => photo.QuoteProjectTask).WithMany(task => task.Photos).HasForeignKey(photo => photo.QuoteProjectTaskId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<QuoteTaskAnalysis>(entity =>
        {
            entity.HasIndex(analysis => new { analysis.QuoteProjectTaskId, analysis.RevisionNumber }).IsUnique();
            entity.HasOne(analysis => analysis.QuoteProjectTask)
                .WithMany(task => task.Analyses)
                .HasForeignKey(analysis => analysis.QuoteProjectTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<QuoteTaskAnalysisMaterial>(entity =>
        {
            entity.HasIndex(item => new { item.QuoteTaskAnalysisId, item.SortOrder }).IsUnique();
            entity.HasOne(item => item.QuoteTaskAnalysis)
                .WithMany(analysis => analysis.Materials)
                .HasForeignKey(item => item.QuoteTaskAnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.VendorProduct)
                .WithMany()
                .HasForeignKey(item => item.VendorProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<QuoteTaskAnalysisReviewItem>(entity =>
        {
            entity.HasIndex(item => new { item.QuoteTaskAnalysisId, item.ItemKey }).IsUnique();
            entity.HasOne(item => item.QuoteTaskAnalysis).WithMany(analysis => analysis.ReviewItems).HasForeignKey(item => item.QuoteTaskAnalysisId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.AddedVendorProduct).WithMany().HasForeignKey(item => item.AddedVendorProductId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<QuotePricingRule>(entity => entity.HasIndex(rule => rule.LocalOperationId).IsUnique());
        builder.Entity<CostingPolicyVersion>(entity =>
        {
            entity.HasIndex(policy => new { policy.LocalOperationId, policy.Name, policy.RevisionNumber }).IsUnique();
            entity.HasOne(policy => policy.LocalOperation).WithMany().HasForeignKey(policy => policy.LocalOperationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CostingPolicyRule>(entity =>
        {
            entity.HasOne(rule => rule.CostingPolicyVersion).WithMany(policy => policy.Rules).HasForeignKey(rule => rule.CostingPolicyVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<StandardSupplyKit>(entity =>
        {
            entity.HasIndex(item => new { item.CostingPolicyVersionId, item.Name }).IsUnique();
            entity.HasOne(item => item.CostingPolicyVersion).WithMany(policy => policy.SupplyKits).HasForeignKey(item => item.CostingPolicyVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<StandardSupplyKitItem>(entity =>
        {
            entity.HasIndex(item => new { item.StandardSupplyKitId, item.VendorProductId }).IsUnique();
            entity.HasOne(item => item.StandardSupplyKit).WithMany(kit => kit.Items).HasForeignKey(item => item.StandardSupplyKitId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.VendorProduct).WithMany().HasForeignKey(item => item.VendorProductId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CrewRateCard>(entity =>
        {
            entity.HasIndex(item => new { item.CostingPolicyVersionId, item.TaskType, item.WorkType }).IsUnique();
            entity.HasOne(item => item.CostingPolicyVersion).WithMany(policy => policy.CrewRates).HasForeignKey(item => item.CostingPolicyVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TaskMarginRule>(entity =>
        {
            entity.HasIndex(item => new { item.CostingPolicyVersionId, item.TaskType, item.WorkType }).IsUnique();
            entity.HasOne(item => item.CostingPolicyVersion).WithMany(policy => policy.MarginRules).HasForeignKey(item => item.CostingPolicyVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<MaterialExclusionRule>(entity =>
        {
            entity.HasIndex(rule => new { rule.MatchPhrase, rule.TaskType }).IsUnique();
        });
        builder.Entity<QuoteTaskAnalysisExclusion>(entity =>
        {
            entity.HasOne(item => item.QuoteTaskAnalysis).WithMany(analysis => analysis.Exclusions).HasForeignKey(item => item.QuoteTaskAnalysisId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.MaterialExclusionRule).WithMany().HasForeignKey(item => item.MaterialExclusionRuleId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<QuoteCostSnapshot>(entity =>
        {
            entity.HasIndex(snapshot => new { snapshot.QuoteVersionId, snapshot.RevisionNumber }).IsUnique();
            entity.HasOne(snapshot => snapshot.QuoteVersion).WithMany(version => version.CostSnapshots).HasForeignKey(snapshot => snapshot.QuoteVersionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(snapshot => snapshot.CostingPolicyVersion).WithMany().HasForeignKey(snapshot => snapshot.CostingPolicyVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<QuoteTaskCostSnapshot>(entity =>
        {
            entity.HasOne(snapshot => snapshot.QuoteCostSnapshot).WithMany(cost => cost.Tasks).HasForeignKey(snapshot => snapshot.QuoteCostSnapshotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(snapshot => snapshot.QuoteProjectTask).WithMany(task => task.CostSnapshots).HasForeignKey(snapshot => snapshot.QuoteProjectTaskId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<QuoteTaskSupplyCostSnapshot>(entity =>
        {
            entity.HasOne(item => item.QuoteTaskCostSnapshot).WithMany(task => task.RequiredSupplies).HasForeignKey(item => item.QuoteTaskCostSnapshotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.VendorProduct).WithMany().HasForeignKey(item => item.VendorProductId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<QuoteAuditEvent>(entity => entity.HasOne(item => item.QuoteCase).WithMany(quote => quote.AuditEvents).HasForeignKey(item => item.QuoteCaseId).OnDelete(DeleteBehavior.Cascade));
        builder.Entity<QuoteProcessingJob>(entity =>
        {
            entity.HasOne(job => job.QuoteCase).WithMany(quote => quote.ProcessingJobs).HasForeignKey(job => job.QuoteCaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(job => job.QuoteProjectTask).WithMany().HasForeignKey(job => job.QuoteProjectTaskId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CentComChatSession>(entity =>
        {
            entity.HasIndex(session => new { session.CreatedByUserId, session.UpdatedAt });
            entity.HasOne(session => session.CreatedByUser).WithMany().HasForeignKey(session => session.CreatedByUserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CentComChatMessage>(entity =>
        {
            entity.HasIndex(message => new { message.CentComChatSessionId, message.CreatedAt });
            entity.HasOne(message => message.CentComChatSession).WithMany(session => session.Messages).HasForeignKey(message => message.CentComChatSessionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<OperationalEvent>(entity =>
        {
            entity.HasIndex(item => item.EventId).IsUnique();
            entity.HasIndex(item => new { item.CorrelationId, item.Timestamp });
            entity.HasIndex(item => new { item.LocalOperationId, item.Timestamp });
            entity.HasOne(item => item.LocalOperation).WithMany().HasForeignKey(item => item.LocalOperationId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<HousecallProJob>(entity =>
        {
            entity.HasIndex(item => new { item.LocalOperationId, item.ExternalId }).IsUnique();
            entity.HasIndex(item => new { item.LocalOperationId, item.WorkStatus });
            entity.HasIndex(item => new { item.LocalOperationId, item.InternalStatus });
            entity.Property(item => item.TotalAmount).HasPrecision(18, 2);
            entity.Property(item => item.JobPrice).HasPrecision(18, 2);
            entity.Property(item => item.OutstandingBalance).HasPrecision(18, 2);
            entity.HasOne(item => item.LocalOperation).WithMany().HasForeignKey(item => item.LocalOperationId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<HousecallProEstimate>(entity =>
        {
            entity.HasIndex(item => new { item.LocalOperationId, item.ExternalId }).IsUnique();
            entity.HasIndex(item => new { item.LocalOperationId, item.Status });
            entity.Property(item => item.TotalAmount).HasPrecision(18, 2);
            entity.HasOne(item => item.LocalOperation).WithMany().HasForeignKey(item => item.LocalOperationId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<HousecallProEstimateCommunication>(entity =>
        {
            entity.HasIndex(item => new { item.HousecallProEstimateId, item.EnteredAt });
            entity.HasOne(item => item.HousecallProEstimate).WithMany(estimate => estimate.Communications).HasForeignKey(item => item.HousecallProEstimateId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<HousecallProEstimateFollowUp>(entity =>
        {
            entity.HasIndex(item => new { item.HousecallProEstimateId, item.EnteredAt });
            entity.HasOne(item => item.HousecallProEstimate).WithMany(estimate => estimate.FollowUps).HasForeignKey(item => item.HousecallProEstimateId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<HousecallProJobFollowUp>(entity =>
        {
            entity.HasIndex(item => new { item.HousecallProJobId, item.EnteredAt });
            entity.HasOne(item => item.HousecallProJob).WithMany(job => job.FollowUps).HasForeignKey(item => item.HousecallProJobId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
