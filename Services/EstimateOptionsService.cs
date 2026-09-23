using System.Security.Claims;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class EstimateOptionsService(
    IDbContextFactory<ApplicationDbContext> factory,
    OperationAccessService access,
    AuthenticationStateProvider authentication)
{
    public async Task<QuoteVersion> LoadAsync(int versionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await LoadAsync(db, versionId);
    }

    private async Task<QuoteVersion> LoadAsync(ApplicationDbContext db, int versionId)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (!principal.IsInRole(ApplicationRoles.Administrator) && !principal.IsInRole(ApplicationRoles.LocalOperator))
            throw new InvalidOperationException("Sign in as an authorized estimator to open this estimate.");
        var allowed = (await access.GetAccessibleOperationsAsync()).Select(operation => operation.Id).ToList();
        return await db.QuoteVersions.AsSplitQuery()
            .Include(version => version.QuoteCase).ThenInclude(quote => quote.ProjectTasks).ThenInclude(task => task.Photos)
            .Include(version => version.Lines)
            .Include(version => version.CostSnapshots).ThenInclude(snapshot => snapshot.Tasks).ThenInclude(task => task.RequiredSupplies)
            .Include(version => version.QuoteCase).ThenInclude(quote => quote.ProjectTasks)
                .ThenInclude(task => task.Analyses).ThenInclude(analysis => analysis.Materials)
                .ThenInclude(material => material.VendorProduct).ThenInclude(product => product!.SupplyVendor)
            .SingleOrDefaultAsync(version => version.Id == versionId && allowed.Contains(version.QuoteCase.LocalOperationId))
            ?? throw new InvalidOperationException("This estimate is not available to your assigned locations.");
    }

    private static async Task RequireDraftAsync(ApplicationDbContext db, QuoteVersion version, string? expectedJson)
    {
        if (version.Status != "Draft" || version.ApprovedAt is not null
            || await db.QuoteVersions.AnyAsync(other => other.QuoteCaseId == version.QuoteCaseId && other.VersionNumber > version.VersionNumber))
            throw new InvalidOperationException("This version is locked. Create or open the latest revision to make changes.");
        if (version.OptionsJson != expectedJson)
            throw new InvalidOperationException("This estimate changed in another session. Reload it before saving.");
        db.Entry(version).Property(item => item.Status).IsModified = true;
    }

    public static EstimateOptions ForEditing(QuoteVersion version)
    {
        var options = version.OptionsJson is null ? new EstimateOptions() : EstimateOptions.Read(version.OptionsJson);
        if (version.Status != "Draft") return options;
        var activeIds = version.QuoteCase.ProjectTasks.Select(task => task.Id).ToHashSet();
        options.Tasks.RemoveAll(task => !activeIds.Contains(task.TaskId));
        foreach (var task in version.QuoteCase.ProjectTasks.OrderBy(task => task.SortOrder))
        {
            var entry = options.Tasks.SingleOrDefault(item => item.TaskId == task.Id);
            if (entry is null)
            {
                entry = new EstimateTaskOptions { TaskId = task.Id };
                options.Tasks.Add(entry);
            }
            if (entry.Measurements != task.Measurements || entry.ScopeOfWork != task.ScopeOfWork)
            {
                entry.SelectedOptionId = null;
                foreach (var option in entry.Options) option.IsReady = false;
            }
            entry.SortOrder = task.SortOrder;
            entry.TaskType = task.TaskType;
            entry.ScopeOfWork = task.ScopeOfWork;
            entry.Measurements = task.Measurements;
        }
        return options;
    }

    public async Task EnableAsync(int versionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, null);
        var options = ForEditing(version);
        var snapshot = version.CostSnapshots.OrderByDescending(item => item.RevisionNumber).FirstOrDefault();
        if (options.Tasks.Count == 0)
            throw new InvalidOperationException("Add project tasks before enabling options.");
        if (version.Lines.Count > 0 && options.Tasks.Count != 1)
            throw new InvalidOperationException("This legacy estimate has unassigned price lines. Keep its current pricing or create a separate task-based estimate.");
        if (snapshot is not null && options.Tasks.Any(task => !snapshot.Tasks.Any(cost => cost.QuoteProjectTaskId == task.TaskId)))
            throw new InvalidOperationException("Keep legacy pricing until every task has a cost snapshot.");
        foreach (var task in options.Tasks)
        {
            if (task.Options.Count == 0) task.Options.Add(new EstimateOption { Name = "Original scope" });
            var option = task.Options.Single();
            var cost = snapshot?.Tasks.SingleOrDefault(item => item.QuoteProjectTaskId == task.TaskId);
            if (cost is not null)
            {
                option.Materials = [new EstimateOptionMaterial { Description = "Materials and supplies from saved estimate", UnitCost = cost.MaterialCost + cost.RequiredSupplyCost }];
                option.LaborCost = cost.LaborCost;
                option.OtherInternalCost = cost.TotalCost - option.MaterialCost - option.LaborCost;
                option.CustomerPrice = cost.SuggestedCustomerPrice;
                option.MarketValue = cost.SuggestedCustomerPrice;
                option.IsReady = true;
                option.CostBasis = $"Preserved cost snapshot {snapshot!.Id}";
            }
            else if (version.Lines.Count > 0)
            {
                option.Materials = version.Lines.Select(line => new EstimateOptionMaterial
                { Description = line.Description, Quantity = line.Quantity, Unit = line.Unit, UnitCost = line.MaterialUnitCost, WastePercent = line.WastePercent }).ToList();
                option.LaborCost = decimal.Round(version.Lines.Sum(line => line.LaborHours * line.LaborRate), 2);
                option.OtherInternalCost = version.Lines.Sum(line => line.EquipmentCost);
                option.CustomerPrice = version.Lines.Sum(line => line.CustomerPrice);
                option.MarketValue = option.CustomerPrice;
                option.IsReady = true;
            }
            if (option.IsReady)
            {
                option.RequiresCentComAnalysis = false;
                option.PricingInputsSignature = option.CalculationSignature();
                task.SelectedOptionId = option.Id;
            }
        }
        if (snapshot is not null)
            options.Tasks[^1].Options[0].CustomerPrice += snapshot.SuggestedCustomerPrice - options.CustomerPrice;
        Validate(options);
        SynchronizePricePlans(options);
        version.OptionsJson = options.Write();
        WriteSelectedLines(version, options);
        await AuditAsync(db, version, "Task options enabled from saved pricing. Prior versions and snapshots retained.");
        await db.SaveChangesAsync();
    }

    public async Task DeleteOptionAsync(int versionId, string expectedJson, int taskId, Guid optionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(expectedJson);
        RequireCurrentTasks(version, document);
        var task = document.Tasks.SingleOrDefault(item => item.TaskId == taskId)
            ?? throw new InvalidOperationException("This task is no longer available.");
        var option = task.Options.SingleOrDefault(item => item.Id == optionId)
            ?? throw new InvalidOperationException("This option was already removed.");
        task.Options.Remove(option);
        if (task.SelectedOptionId == optionId) task.SelectedOptionId = null;
        SynchronizePricePlans(document);
        version.OptionsJson = document.Write();
        WriteSelectedLines(version, document);
        await db.QuoteProcessingJobs.Where(job => job.QuoteCaseId == version.QuoteCaseId
                && job.EstimateOptionId == optionId && job.Status == "Queued")
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Status, "Cancelled")
                .SetProperty(job => job.Message, "Option deleted before analysis."));
        await AuditAsync(db, version, $"Option '{option.Name}' deleted from task {task.SortOrder}.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SetRailingAsync(int versionId, string expectedJson, int taskId, Guid optionId, bool include)
    {
        var version = await LoadAsync(versionId);
        if (version.OptionsJson != expectedJson) throw new InvalidOperationException("The task changed. Reload before changing railing.");
        var document = ForEditing(version);
        var option = document.Tasks.Single(task => task.TaskId == taskId).Options.Single(item => item.Id == optionId);
        if (option.AutomaticMaterial is null) throw new InvalidOperationException("This saved option retains its existing material specification.");
        option.IncludeRailing = include;
        option.IsReady = false;
        option.SourceAnalysisId = null;
        option.PricingInputsSignature = null;
        await SaveAsync(versionId, expectedJson, document, version.TaxRate, version.DiscountAmount);
    }

    public async Task SaveScopeAsync(int versionId, string expectedJson, EstimateOptions edited,
        decimal taxRate, decimal discount, int? taskId, bool summaryOnly, string? projectOverview = null)
    {
        if (taskId is null && !summaryOnly)
        {
            await SaveAsync(versionId, expectedJson, edited, taxRate, discount, projectOverview);
            return;
        }
        var current = await LoadAsync(versionId);
        if (current.OptionsJson is null) throw new InvalidOperationException("Reload the estimate before saving.");
        var original = EstimateOptions.Read(expectedJson);
        var merged = ForEditing(current);
        if (taskId is int id)
        {
            var before = original.Tasks.SingleOrDefault(task => task.TaskId == id);
            var saved = EstimateOptions.Read(current.OptionsJson).Tasks.SingleOrDefault(task => task.TaskId == id);
            if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(saved))
                throw new InvalidOperationException("This task changed while you were editing. Reload its saved options before saving.");
            var candidate = edited.Tasks.SingleOrDefault(task => task.TaskId == id)
                ?? throw new InvalidOperationException("This task is no longer available.");
            var index = merged.Tasks.FindIndex(task => task.TaskId == id);
            if (index < 0) throw new InvalidOperationException("This task was removed. Reload the estimate.");
            merged.Tasks[index] = candidate;
            // Other task editors may have saved since this editor loaded.
            await SaveAsync(versionId, current.OptionsJson, merged, current.TaxRate, current.DiscountAmount);
        }
        else
        {
            if (!merged.HasCalculatedCosts)
                throw new InvalidOperationException("Add tasks and calculate their option costs before saving the estimate.");
            // Shared totals never submit another task editor's stale option snapshot.
            if (current.OptionsJson != expectedJson)
                throw new InvalidOperationException("The estimate changed. Reload saved totals before saving.");
            await SaveAsync(versionId, current.OptionsJson, merged, taxRate, discount, projectOverview);
        }
    }

    public async Task SaveAsync(int versionId, string expectedJson, EstimateOptions options, decimal taxRate, decimal discount, string? projectOverview = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        Validate(options);
        RequireCurrentTasks(version, options);
        if (taxRate < 0 || taxRate > 100 || discount < 0 || discount != decimal.Round(discount, 2))
            throw new InvalidOperationException("Enter a tax rate between 0 and 100 and a nonnegative discount in dollars and cents.");
        var previous = EstimateOptions.Read(expectedJson);
        var previousIds = previous.Tasks.SelectMany(task => task.Options.Select(option => new { task.TaskId, option.Id }))
            .ToDictionary(item => item.Id, item => item.TaskId);
        if (options.Tasks.Any(task => task.Options.Any(option => previousIds.TryGetValue(option.Id, out var originalTaskId) && originalTaskId != task.TaskId)))
            throw new InvalidOperationException("An existing option cannot be moved to another task.");
        foreach (var entry in options.Tasks)
        {
            var old = previous.Tasks.SingleOrDefault(item => item.TaskId == entry.TaskId);
            entry.SortOrder = version.QuoteCase.ProjectTasks.Single(task => task.Id == entry.TaskId).SortOrder;
            foreach (var option in entry.Options)
            {
                var original = old?.Options.SingleOrDefault(item => item.Id == option.Id);
                if (JsonSerializer.Serialize(original?.CopySource) != JsonSerializer.Serialize(option.CopySource))
                    throw new InvalidOperationException("Use Copy from option to create a new copy. Its source material snapshot cannot be edited.");
                option.RequiresCentComAnalysis |= original?.RequiresCentComAnalysis ?? true;
            }
            entry.SelectedOptionId = old?.SelectedOptionId;
            var before = old?.Options.SingleOrDefault(option => option.Id == old.SelectedOptionId);
            var after = entry.Options.SingleOrDefault(option => option.Id == entry.SelectedOptionId);
            if (after is null || !after.IsReady || old?.Measurements != entry.Measurements
                || old?.ScopeOfWork != entry.ScopeOfWork || JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
                entry.SelectedOptionId = null;
        }
        if (previous.Tasks.Count > 0 && (!previous.Tasks.Select(task => task.TaskId).ToHashSet().SetEquals(options.Tasks.Select(task => task.TaskId))
            || options.Tasks.Any(task => previous.Tasks.SingleOrDefault(item => item.TaskId == task.TaskId)?.IsRequired != task.IsRequired)))
        {
            // Task membership affects allocation of fixed project overhead.
            foreach (var task in options.Tasks)
            {
                task.SelectedOptionId = null;
                foreach (var option in task.Options) option.IsReady = false;
            }
        }
        // A change to commercial terms requires choices to be reconfirmed.
        if (version.TaxRate != taxRate || version.DiscountAmount != discount)
            foreach (var task in options.Tasks) task.SelectedOptionId = null;
        if (projectOverview is not null)
        {
            if (projectOverview.Length > 2000) throw new InvalidOperationException("Project overview must be 2000 characters or fewer.");
            if (version.QuoteCase.WorkDescription != projectOverview.Trim())
            {
                foreach (var task in options.Tasks)
                {
                    task.SelectedOptionId = null;
                    foreach (var option in task.Options) option.IsReady = false;
                }
                version.QuoteCase.WorkDescription = projectOverview.Trim();
            }
        }
        await RequireAcceptedAnalysesAsync(db, version, options);
        version.TaxRate = taxRate;
        version.DiscountAmount = discount;
        SynchronizePricePlans(options);
        version.OptionsJson = options.Write();
        WriteSelectedLines(version, options);
        await AuditAsync(db, version, "Task options, separate costs, market values, and customer prices saved. Changed selections require homeowner review.");
        await db.SaveChangesAsync();
    }

    public async Task<bool> DefaultSingleOptionPlansAsync(int versionId, string expectedJson)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(expectedJson);
        if (!document.Tasks.Any(task => task.IsRequired && task.Options.Count == 1 && task.PriceOptionId is null)) return false;
        SynchronizePricePlans(document);
        await RequireAcceptedAnalysesAsync(db, version, document);
        version.OptionsJson = document.Write();
        WriteSelectedLines(version, document);
        await AuditAsync(db, version, "Single-option task pricing plans defaulted to their only option.");
        await db.SaveChangesAsync();
        return true;
    }

    public async Task SelectPricePlanAsync(int versionId, string expectedJson, int taskId, Guid? optionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(expectedJson);
        RequireCurrentTasks(version, document);
        var task = document.Tasks.SingleOrDefault(item => item.TaskId == taskId)
            ?? throw new InvalidOperationException("This task is no longer available.");
        if (optionId is not null && !task.Options.Any(option => option.Id == optionId))
            throw new InvalidOperationException("Choose an option belonging to this task.");
        task.PriceOptionId = optionId;
        task.SelectedOptionId = null;
        SynchronizePricePlans(document);
        await RequireAcceptedAnalysesAsync(db, version, document);
        SynchronizePricePlans(document);
        version.OptionsJson = document.Write();
        WriteSelectedLines(version, document);
        await AuditAsync(db, version, $"Pricing plan selected for task {task.SortOrder}.");
        await db.SaveChangesAsync();
    }

    public async Task<(bool Changed, string? Message)> CalculatePricePlansAsync(int versionId, string expectedJson)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(expectedJson);
        RequireCurrentTasks(version, document);
        var queue = new List<Guid>();
        var notices = new List<string>();
        var changed = false;
        foreach (var entry in document.Tasks)
        {
            var option = entry.Options.SingleOrDefault(item => item.Id == entry.PriceOptionId);
            if (option is null) continue;
            if (option.IsReady && !option.RequiresCentComAnalysis && (!option.HasPlanningInputs || option.IsCalculationCurrent)) continue;
            var task = version.QuoteCase.ProjectTasks.Single(item => item.Id == entry.TaskId);
            var analysis = await db.QuoteTaskAnalyses
                .Include(item => item.Materials).ThenInclude(item => item.VendorProduct).ThenInclude(item => item!.SupplyVendor)
                .Include(item => item.ReviewItems)
                .Where(item => item.QuoteProjectTaskId == task.Id && item.EstimateOptionId == option.Id)
                .OrderByDescending(item => item.RevisionNumber).FirstOrDefaultAsync();
            var current = analysis?.InputSignature == EstimateOptions.AnalysisSignature(task, option);
            if (option.IsReady && option.IsCalculationCurrent && current && option.SourceAnalysisId == analysis!.Id
                && (analysis.Status == QuoteTaskAnalysisStatuses.Accepted && !option.IsProvisionalPrice
                    || analysis.Status == QuoteTaskAnalysisStatuses.NeedsReview && option.IsProvisionalPrice)) continue;
            if (option.IsReady || entry.SelectedOptionId is not null)
            {
                option.IsReady = false;
                entry.SelectedOptionId = null;
                changed = true;
            }
            if (analysis is null || !current)
            {
                if (!string.IsNullOrWhiteSpace(task.ScopeOfWork)) queue.Add(option.Id);
                else notices.Add($"Enter a scope of work for task {entry.SortOrder} to calculate its price.");
                continue;
            }
            if (analysis.Status is QuoteTaskAnalysisStatuses.Queued or QuoteTaskAnalysisStatuses.Processing)
            {
                notices.Add($"CentCom is calculating task {entry.SortOrder}. Prices will update automatically.");
                continue;
            }
            if (analysis.Status is not QuoteTaskAnalysisStatuses.Accepted and not QuoteTaskAnalysisStatuses.NeedsReview)
            {
                notices.Add($"Retry the analysis for task {entry.SortOrder} before its price can be calculated.");
                continue;
            }
            var materials = analysis.Materials.Where(item => !item.IsRemoved).ToList();
            if (materials.Count == 0 || materials.Any(item => item.UnitCost <= 0 || item.Quantity <= 0))
            {
                notices.Add($"Task {entry.SortOrder} needs material prices in its CentCom analysis before the estimate can be calculated.");
                continue;
            }
            CopyMaterials(option, analysis);
            option.AdditionalBaselineCost = analysis.DeliveryAllowance + analysis.TaxAllowance + analysis.OtherAllowance
                + analysis.ReviewItems.Sum(item => item.AdditionalFeeAmount);
            await PriceOptionAsync(db, version, document, entry, option, task);
            option.IsReady = true;
            option.IsProvisionalPrice = analysis.Status != QuoteTaskAnalysisStatuses.Accepted;
            changed = true;
        }
        if (changed)
        {
            SynchronizePricePlans(document);
            version.OptionsJson = document.Write();
            WriteSelectedLines(version, document);
            await AuditAsync(db, version, "Selected option plans calculated using CentCom materials and the costing policy.");
            await db.SaveChangesAsync();
        }
        foreach (var optionId in queue)
        {
            var latest = await LoadAsync(versionId);
            if (await QueueAnalysesAsync(versionId, latest.OptionsJson!, optionId) > 0) changed = true;
        }
        return (changed, notices.Count > 0 ? string.Join(" ", notices) : null);
    }

    private static void SynchronizePricePlans(EstimateOptions document)
    {
        foreach (var task in document.Tasks)
        {
            if (task.PriceOptionId is not null && !task.Options.Any(option => option.Id == task.PriceOptionId))
                task.PriceOptionId = null;
            if (task.PriceOptionId is null && task.IsRequired && task.Options.Count == 1)
                task.PriceOptionId = task.Options[0].Id;
            if (task.PriceOptionId is not null)
            {
                var planned = task.Options.Single(option => option.Id == task.PriceOptionId);
                task.SelectedOptionId = planned.IsReady && (!planned.HasPlanningInputs || planned.IsCalculationCurrent)
                    ? planned.Id : null;
            }
        }
    }

    public async Task SelectAsync(int versionId, string expectedJson, int taskId, Guid? optionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var options = EstimateOptions.Read(expectedJson);
        RequireCurrentTasks(version, options);
        var task = options.Tasks.SingleOrDefault(item => item.TaskId == taskId)
            ?? throw new InvalidOperationException("The selected task does not belong to this estimate.");
        if (optionId is not null && !task.Options.Any(option => option.Id == optionId && option.IsReady))
            throw new InvalidOperationException("Choose a priced option offered for this task.");
        await RequireAcceptedAnalysesAsync(db, version, options);
        task.SelectedOptionId = optionId;
        task.PriceOptionId = optionId;
        SynchronizePricePlans(options);
        version.OptionsJson = options.Write();
        WriteSelectedLines(version, options);
        await AuditAsync(db, version, $"Homeowner choice recorded by estimator for task {task.SortOrder}: {task.Options.SingleOrDefault(option => option.Id == optionId)?.Name ?? "No selection"}.");
        await db.SaveChangesAsync();
    }

    public async Task ApproveAsync(int versionId, string expectedJson)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var options = EstimateOptions.Read(expectedJson);
        Validate(options);
        RequireCurrentTasks(version, options);
        if (!options.IsComplete || !options.Selected.Any())
            throw new InvalidOperationException("Choose one priced option for every required task before accepting the estimate.");
        if (version.DiscountAmount > options.CustomerPrice)
            throw new InvalidOperationException("The discount exceeds the selected price. Update the discount before acceptance.");
        await RequireAcceptedAnalysesAsync(db, version, options, requireApproval: true);
        WriteSelectedLines(version, options);
        version.Status = "Approved";
        version.ApprovedAt = DateTimeOffset.UtcNow;
        version.ApprovedByUserId = (await authentication.GetAuthenticationStateAsync()).User.FindFirstValue(ClaimTypes.NameIdentifier);
        await AuditAsync(db, version, $"Homeowner selections accepted and version locked at {version.Total:C2}.", QuoteStatuses.Approved);
        await db.SaveChangesAsync();
    }

    public async Task CopyOptionAsync(int versionId, string expectedJson, int taskId, Guid sourceOptionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(expectedJson);
        RequireCurrentTasks(version, document);
        var task = document.Tasks.SingleOrDefault(item => item.TaskId == taskId)
            ?? throw new InvalidOperationException("Choose a task in this estimate.");
        var source = task.Options.SingleOrDefault(item => item.Id == sourceOptionId)
            ?? throw new InvalidOperationException("Copy from an existing option in the same task.");
        if (source.Materials.Count == 0 || (!source.IsReady && (source.SourceAnalysisId is null || !source.IsCalculationCurrent)))
            throw new InvalidOperationException("Finish the source option's material analysis and price calculation before copying it.");
        if (source.RequiresCentComAnalysis)
        {
            var projectTask = version.QuoteCase.ProjectTasks.Single(item => item.Id == taskId);
            var latest = await db.QuoteTaskAnalyses.AsNoTracking()
                .Where(item => item.QuoteProjectTaskId == taskId && item.EstimateOptionId == source.Id)
                .OrderByDescending(item => item.RevisionNumber).FirstOrDefaultAsync();
            if (latest is null || latest.Id != source.SourceAnalysisId || latest.Status != QuoteTaskAnalysisStatuses.Accepted
                || latest.InputSignature != EstimateOptions.AnalysisSignature(projectTask, source))
                throw new InvalidOperationException("Review and use the source option's current CentCom materials before copying.");
        }
        var copy = JsonSerializer.Deserialize<EstimateOption>(JsonSerializer.Serialize(source))!;
        copy.Id = Guid.NewGuid();
        copy.Name = $"Option {task.Options.Count + 1}";
        copy.CopySource = new EstimateOptionCopySource
        {
            OptionId = source.Id, TaskId = taskId, VersionId = versionId, Name = source.Name,
            Description = source.Description, AnalysisId = source.SourceAnalysisId, AdditionalBaselineCost = source.AdditionalBaselineCost,
            Materials = JsonSerializer.Deserialize<List<EstimateOptionMaterial>>(JsonSerializer.Serialize(source.Materials))!
        };
        copy.Substitutions = [];
        copy.SourceAnalysisId = null;
        copy.PricingInputsSignature = null;
        copy.RequiresCentComAnalysis = true;
        copy.IsReady = false;
        task.Options.Add(copy);
        SynchronizePricePlans(document);
        version.OptionsJson = document.Write();
        await AuditAsync(db, version, $"Task {task.SortOrder}: '{copy.Name}' copied from '{source.Name}' with independent materials, crew, duration, costs, and prices. Substitution analysis required.");
        await db.SaveChangesAsync();
    }

    public async Task<int> QueueAnalysesAsync(int versionId, string expectedJson, Guid? optionId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(expectedJson);
        RequireCurrentTasks(version, document);
        var targets = document.Tasks.SelectMany(task => task.Options.Select(option => (Task: task, Option: option)))
            .Where(item => optionId is null || item.Option.Id == optionId).ToList();
        if (targets.Count == 0) throw new InvalidOperationException("Save the task options before requesting CentCom calculations.");
        var pendingJobs = await db.QuoteProcessingJobs.Where(job => job.QuoteCaseId == version.QuoteCaseId
                && job.EstimateOptionId != null && (job.Status == "Queued" || job.Status == "Processing"))
            .Select(job => new { OptionId = job.EstimateOptionId!.Value, job.QuoteTaskAnalysisId }).ToListAsync();
        var pendingAnalysisIds = pendingJobs.Select(job => job.QuoteTaskAnalysisId).ToList();
        var pendingAnalyses = await db.QuoteTaskAnalyses.Where(item => pendingAnalysisIds.Contains(item.Id)).ToListAsync();
        var pendingIds = targets.Where(item => pendingJobs.Any(job => job.OptionId == item.Option.Id
            && pendingAnalyses.Any(analysis => analysis.Id == job.QuoteTaskAnalysisId
                && analysis.InputSignature == EstimateOptions.AnalysisSignature(
                    version.QuoteCase.ProjectTasks.Single(task => task.Id == item.Task.TaskId), item.Option))))
            .Select(item => item.Option.Id).ToList();
        var userId = (await authentication.GetAuthenticationStateAsync()).User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reserved = new List<(QuoteProjectTask Task, EstimateOption Option, QuoteTaskAnalysis Analysis)>();
        foreach (var group in targets.Where(item => !pendingIds.Contains(item.Option.Id)).GroupBy(item => item.Task.TaskId))
        {
            var task = version.QuoteCase.ProjectTasks.Single(item => item.Id == group.Key);
            if (string.IsNullOrWhiteSpace(task.ScopeOfWork))
                throw new InvalidOperationException($"Enter the scope for task {task.SortOrder} before requesting CentCom calculations.");
            var revision = await db.QuoteTaskAnalyses.Where(item => item.QuoteProjectTaskId == task.Id)
                .Select(item => (int?)item.RevisionNumber).MaxAsync() ?? 0;
            foreach (var item in group)
            {
                if (item.Option.CrewCount is not null && item.Option.AutomaticMaterial is null)
                    throw new InvalidOperationException($"Choose a material for '{item.Option.Name}' before analysis.");
                var analysis = new QuoteTaskAnalysis
                {
                    QuoteProjectTaskId = task.Id, EstimateOptionId = item.Option.Id,
                    RevisionNumber = ++revision, Status = QuoteTaskAnalysisStatuses.Queued,
                    InputSignature = EstimateOptions.AnalysisSignature(task, item.Option), SubmittedByUserId = userId
                };
                db.QuoteTaskAnalyses.Add(analysis);
                item.Option.RequiresCentComAnalysis = true;
                item.Option.IsReady = false;
                item.Option.SourceAnalysisId = null;
                if (item.Task.SelectedOptionId == item.Option.Id) item.Task.SelectedOptionId = null;
                reserved.Add((task, item.Option, analysis));
            }
        }
        if (reserved.Count == 0) return 0;
        SynchronizePricePlans(document);
        version.OptionsJson = document.Write();
        WriteSelectedLines(version, document);
        await AuditAsync(db, version, $"{reserved.Count} separate option calculation(s) queued for CentCom.");
        await db.SaveChangesAsync();
        foreach (var item in reserved)
        {
            db.QuoteProcessingJobs.Add(new QuoteProcessingJob
            {
                QuoteCaseId = version.QuoteCaseId, QuoteProjectTaskId = item.Task.Id,
                EstimateOptionId = item.Option.Id, QuoteTaskAnalysisId = item.Analysis.Id,
                JobType = "CentCom Task Analysis", Status = "Queued",
                Message = $"Task {item.Task.SortOrder}, option '{item.Option.Name}', analysis revision {item.Analysis.RevisionNumber}."
            });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return reserved.Count;
    }

    private static async Task RequireAcceptedAnalysesAsync(ApplicationDbContext db, QuoteVersion version, EstimateOptions document, bool requireApproval = false)
    {
        foreach (var entry in document.Tasks)
        {
            foreach (var option in entry.Options.Where(item => item.IsReady && item.RequiresCentComAnalysis))
            {
                var task = version.QuoteCase.ProjectTasks.Single(item => item.Id == entry.TaskId);
                var analysis = await db.QuoteTaskAnalyses.AsNoTracking()
                    .Where(item => item.QuoteProjectTaskId == task.Id && item.EstimateOptionId == option.Id)
                    .OrderByDescending(item => item.RevisionNumber).FirstOrDefaultAsync();
                if (analysis is null || analysis.Id != option.SourceAnalysisId || (analysis.Status != QuoteTaskAnalysisStatuses.Accepted
                        && (requireApproval || !option.IsProvisionalPrice || analysis.Status != QuoteTaskAnalysisStatuses.NeedsReview))
                    || analysis.InputSignature != EstimateOptions.AnalysisSignature(task, option))
                    throw new InvalidOperationException($"Calculate and review '{option.Name}' with CentCom, then load its accepted materials and costs before marking it ready.");
            }
        }
    }

    private static void RequireCurrentTasks(QuoteVersion version, EstimateOptions options)
    {
        var tasks = version.QuoteCase.ProjectTasks.ToDictionary(task => task.Id);
        if (!tasks.Keys.ToHashSet().SetEquals(options.Tasks.Select(task => task.TaskId))
            || options.Tasks.Any(item => !tasks.TryGetValue(item.TaskId, out var task)
                || item.ScopeOfWork != task.ScopeOfWork || item.Measurements != task.Measurements || item.TaskType != task.TaskType))
            throw new InvalidOperationException("Tasks or measurements changed. Reload, save, and review the task options before continuing.");
    }

    public static void CopyMaterials(EstimateOption option, QuoteTaskAnalysis analysis)
    {
        option.Materials = analysis.Materials.Where(material => !material.IsRemoved).OrderBy(material => material.SortOrder)
            .Select(material => new EstimateOptionMaterial
            {
                Description = material.Description, Quantity = material.Quantity, Unit = material.Unit,
                UnitCost = material.UnitCost, WastePercent = material.WastePercent, VendorSku = material.VendorSku,
                VendorName = material.VendorProduct?.SupplyVendor?.Name ?? material.SourceType ?? "Manual materials",
                IsPolicySupply = material.SourceReference == "Copied option policy supply"
            }).ToList();
        option.SourceAnalysisId = analysis.Id;
        option.IsReady = false;
    }

    public async Task<EstimateOption> PriceAcceptedAnalysisAsync(int versionId, string expectedJson, int taskId, Guid optionId, EstimateOption? editedOption = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(expectedJson);
        RequireCurrentTasks(version, document);
        var offeredTask = document.Tasks.SingleOrDefault(task => task.TaskId == taskId);
        var option = offeredTask?.Options.SingleOrDefault(option => option.Id == optionId)
            ?? throw new InvalidOperationException("Save this option before loading its analysis.");
        if (editedOption is not null)
        {
            if (editedOption.Id != optionId) throw new InvalidOperationException("Choose an option belonging to this task.");
            option = JsonSerializer.Deserialize<EstimateOption>(JsonSerializer.Serialize(editedOption))!;
        }
        var task = version.QuoteCase.ProjectTasks.Single(task => task.Id == taskId);
        var analysis = await db.QuoteTaskAnalyses
            .Include(item => item.Materials).ThenInclude(item => item.VendorProduct).ThenInclude(item => item!.SupplyVendor)
            .Include(item => item.ReviewItems)
            .Where(item => item.QuoteProjectTaskId == taskId && item.EstimateOptionId == optionId)
            .OrderByDescending(item => item.RevisionNumber).FirstOrDefaultAsync();
        if (analysis?.Status != QuoteTaskAnalysisStatuses.Accepted)
            throw new InvalidOperationException("Analyze this option and accept its latest material analysis before importing it.");
        if (analysis.InputSignature != EstimateOptions.AnalysisSignature(task, option))
            throw new InvalidOperationException("The task measurements, scope, or option changed after analysis. Analyze and review this option again.");
        CopyMaterials(option, analysis);
        option.AdditionalBaselineCost = analysis.DeliveryAllowance + analysis.TaxAllowance + analysis.OtherAllowance
            + analysis.ReviewItems.Sum(item => item.AdditionalFeeAmount);
        return await PriceOptionAsync(db, version, document, offeredTask!, option, task);
    }

    public async Task<EstimateOption> CalculateOptionPriceAsync(int versionId, string expectedJson,
        EstimateOptions draft, int taskId, Guid optionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var version = await LoadAsync(db, versionId);
        await RequireDraftAsync(db, version, expectedJson);
        var document = EstimateOptions.Read(JsonSerializer.Serialize(draft));
        Validate(document, requireCurrentPrices: false);
        RequireCurrentTasks(version, document);
        var offeredTask = document.Tasks.SingleOrDefault(item => item.TaskId == taskId)
            ?? throw new InvalidOperationException("Choose a task in this estimate.");
        var option = offeredTask.Options.SingleOrDefault(item => item.Id == optionId)
            ?? throw new InvalidOperationException("Choose an option in this task.");
        var task = version.QuoteCase.ProjectTasks.Single(item => item.Id == taskId);
        return await PriceOptionAsync(db, version, document, offeredTask, option, task);
    }

    private static async Task<EstimateOption> PriceOptionAsync(ApplicationDbContext db, QuoteVersion version,
        EstimateOptions document, EstimateTaskOptions offeredTask, EstimateOption option, QuoteProjectTask task)
    {
        // Work from a detached candidate. Nothing is persisted until the estimator saves.
        option.IsReady = false;
        Validate(new EstimateOptions { Tasks = [new EstimateTaskOptions { Options = [option] }] }, requireCurrentPrices: false);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var policy = await db.CostingPolicyVersions.AsNoTracking()
            .Include(item => item.Rules).Include(item => item.CrewRates).Include(item => item.MarginRules)
            .Include(item => item.SupplyKits).ThenInclude(item => item.Items).ThenInclude(item => item.VendorProduct).ThenInclude(item => item.Prices)
            .Include(item => item.SupplyKits).ThenInclude(item => item.Items).ThenInclude(item => item.VendorProduct).ThenInclude(item => item.Product)
            .Include(item => item.SupplyKits).ThenInclude(item => item.Items).ThenInclude(item => item.VendorProduct).ThenInclude(item => item.SupplyVendor)
            .Where(item => item.IsActive && item.EffectiveDate <= today && (item.EndDate == null || item.EndDate >= today)
                && (item.LocalOperationId == version.QuoteCase.LocalOperationId || item.LocalOperationId == null))
            .OrderByDescending(item => item.LocalOperationId == version.QuoteCase.LocalOperationId)
            .ThenByDescending(item => item.EffectiveDate).ThenByDescending(item => item.RevisionNumber).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Configure an active costing policy before pricing an analysis.");
        if (!option.HasPlanningInputs) option.WorkType ??= task.WorkType;
        if (option.CopySource is null && option.AutomaticMaterial is null) option.Materials.RemoveAll(material => material.IsPolicySupply);
        foreach (var kit in policy.SupplyKits.Where(kit => option.CopySource is null && option.AutomaticMaterial is null && kit.IsActive && (kit.TaskType == null || kit.TaskType == task.TaskType)
            && (kit.WorkType == null || kit.WorkType == option.WorkType)))
        {
            foreach (var item in kit.Items)
            {
                var price = item.VendorProduct.Prices.Where(price => price.EffectiveDate <= today
                    && (price.ExpirationDate == null || price.ExpirationDate >= today)).OrderByDescending(price => price.EffectiveDate).FirstOrDefault()
                    ?? throw new InvalidOperationException($"Supply kit '{kit.Name}' contains an item without a current price.");
                option.Materials.Add(new EstimateOptionMaterial
                {
                    Description = item.VendorProduct.Product.Name, VendorName = item.VendorProduct.SupplyVendor.Name,
                    Quantity = item.Quantity, UnitCost = price.UnitPrice, WastePercent = item.WastePercent, IsPolicySupply = true
                });
            }
        }
        var crew = policy.CrewRates.Where(rate => rate.IsActive && rate.TaskType == task.TaskType
            && (rate.WorkType == null || rate.WorkType == option.WorkType)).OrderByDescending(rate => rate.WorkType != null).FirstOrDefault();
        var policyCrew = crew?.CrewSize ?? policy.DefaultCrewSize;
        if (policyCrew <= 0) throw new InvalidOperationException("The costing policy needs a positive crew size.");
        option.EstimatedDays ??= 1m;
        option.CrewSize ??= policyCrew;
        option.DailyCostPerCrewMember ??= decimal.Round((crew?.DailyCrewCost ?? policy.DefaultDailyCrewCost) / policyCrew, 4);
        var days = option.EstimatedDays.Value;
        option.LaborCost = decimal.Round(days * (option.CrewCount ?? 1) * option.CrewSize.Value * option.DailyCostPerCrewMember.Value, 2);
        var direct = option.MaterialCost + option.LaborCost;
        var overhead = policy.Rules.Where(rule => rule.IsActive && rule.Scope == CostRuleScopes.Task
            && (rule.TaskType == null || rule.TaskType == task.TaskType)).Sum(rule => Calculate(rule, direct, days, 1))
            + option.AdditionalBaselineCost;
        var contingency = decimal.Round((direct + overhead) * policy.DefaultContingencyPercent / 100m, 2);
        // Fixed project costs are allocated once across required tasks. Variable
        // costs belong to each option; alternatives are never added together.
        var requiredCount = document.Tasks.Count(item => item.IsRequired);
        var share = requiredCount == 0 ? 1m / Math.Max(1, document.Tasks.Count)
            : offeredTask!.IsRequired ? 1m / requiredCount : 0m;
        var project = policy.GeneralOverheadFixed * share
            + (policy.GeneralOverheadPerProjectDay + policy.CalculatedOverheadPerCrewDay * (option.CrewCount ?? 1)) * days
            + (direct + overhead) * policy.GeneralOverheadPercent / 100m
            + policy.Rules.Where(rule => rule.IsActive && rule.Scope == CostRuleScopes.Project
                && (rule.TaskType == null || rule.TaskType == task.TaskType))
                .Sum(rule => Calculate(rule, direct + overhead, days, rule.TaskType == null ? share : 1m / Math.Max(1, document.Tasks.Count(item => item.IsRequired && item.TaskType == rule.TaskType))));
        option.OtherInternalCost = decimal.Round(overhead + contingency + project, 2);
        var margin = option.TargetMarginPercent ?? policy.MarginRules.Where(rule => rule.IsActive && rule.TaskType == task.TaskType
            && (rule.WorkType == null || rule.WorkType == option.WorkType)).OrderByDescending(rule => rule.WorkType != null)
            .Select(rule => (decimal?)rule.TargetMarginPercent).FirstOrDefault() ?? policy.DefaultTargetMarginPercent;
        if (margin < 0 || margin >= 100) throw new InvalidOperationException("The costing policy margin must be between 0 and 100 percent.");
        option.TargetMarginPercent = margin;
        option.CustomerPrice = decimal.Round(option.InternalCost / (1 - margin / 100m), 2);
        if (option.MarketValue == 0) option.MarketValue = option.CustomerPrice;
        option.CostBasis = $"{policy.Name} revision {policy.RevisionNumber}; {option.CrewCount ?? 1} crews x {option.CrewSize:N2} members per crew x {days:N2} workdays x {option.DailyCostPerCrewMember:C2} per person/day; fixed project overhead allocated across {requiredCount} required tasks. Market value starts at policy price and requires estimator review.";
        option.PricingInputsSignature = option.CalculationSignature();
        return option;
    }

    private static decimal Calculate(CostingPolicyRule rule, decimal direct, decimal days, decimal fixedShare) =>
        decimal.Round(rule.CalculationMethod switch
        {
            CostRuleCalculationMethods.PerProjectDay => rule.Rate * days,
            CostRuleCalculationMethods.PercentOfDirectCost => direct * rule.Rate / 100m,
            _ => rule.Rate * fixedShare
        }, 2);

    private static void Validate(EstimateOptions options, bool requireCurrentPrices = true)
    {
        if (options.Tasks.Select(task => task.TaskId).Distinct().Count() != options.Tasks.Count
            || options.Tasks.SelectMany(task => task.Options).Select(option => option.Id).Distinct().Count() != options.Tasks.Sum(task => task.Options.Count))
            throw new InvalidOperationException("Tasks and options must have unique identifiers.");
        foreach (var task in options.Tasks)
        {
            // Draft tasks may have no options until the estimator adds one.
            foreach (var option in task.Options)
            {
                if (option.AutomaticMaterial is not null && option.AutomaticMaterial is not ("Trex" or "Trex Enhance" or "Pressure-treated wood" or "Deckorators"))
                    throw new InvalidOperationException("Choose Trex Enhance, Wood, or Deckorators.");
                if (option.Substitutions.Distinct().Count() != option.Substitutions.Count
                    || option.Substitutions.Any(key => !EstimateOptionSubstitutions.All.Any(item => item.Key == key))
                    || (option.Substitutions.Contains(EstimateOptionSubstitutions.Trex) && option.Substitutions.Contains(EstimateOptionSubstitutions.Deckorators)))
                    throw new InvalidOperationException("Choose at most one decking replacement and only the available material substitutions.");
                if (option.Substitutions.Any(key => !EstimateOptionSubstitutions.CanApply(option.CopySource, key)))
                    throw new InvalidOperationException("The copied material list must identify the wood decking or wood railing being replaced. Update and recopy the source option if necessary.");
                option.WorkType = string.IsNullOrWhiteSpace(option.WorkType) ? null : option.WorkType.Trim();
                if (option.CrewCount is <= 0 or > 1000 || option.EstimatedDays is < 0 or > 100000 || option.CrewSize is <= 0 or > 10000
                    || option.DailyCostPerCrewMember is < 0 or > 99999999 || option.TargetMarginPercent is < 0 or >= 100
                    || option.AdditionalBaselineCost < 0 || option.AdditionalBaselineCost > 999999999999m
                    || option.AdditionalBaselineCost != decimal.Round(option.AdditionalBaselineCost, 2)
                    || (option.WorkType is not null && !ProjectWorkTypes.All.Contains(option.WorkType)))
                    throw new InvalidOperationException("Enter valid option days, a positive crew size, nonnegative labor and baseline costs, and a margin below 100 percent.");
                if (requireCurrentPrices && option.IsReady && option.HasPlanningInputs && !option.IsCalculationCurrent)
                    throw new InvalidOperationException($"Recalculate '{option.Name}' after changing its planning or baseline costs, then review its customer price.");
                if (option.Id == Guid.Empty || string.IsNullOrWhiteSpace(option.Name) || option.Name.Length > 160 || option.Description.Length > 4000)
                    throw new InvalidOperationException("Each option needs a name of up to 160 characters and a description of up to 4000 characters.");
                if (new[] { option.LaborCost, option.OtherInternalCost, option.MarketValue, option.CustomerPrice }.Any(value => value < 0 || value > 999999999999m || value != decimal.Round(value, 2)))
                    throw new InvalidOperationException("Option costs, market value, and customer price must be nonnegative dollars and cents.");
                if (option.Materials.Any(material => string.IsNullOrWhiteSpace(material.Description) || material.Description.Length > 500
                    || string.IsNullOrWhiteSpace(material.Unit) || material.Unit.Length > 40 || material.Quantity <= 0 || material.Quantity > 1000000m || material.UnitCost < 0 || material.UnitCost > 99999999m
                    || material.WastePercent < 0 || material.WastePercent > 100))
                    throw new InvalidOperationException("Each material needs a description, unit, positive quantity, nonnegative unit cost, and waste between 0 and 100 percent.");
            }
        }
    }

    internal static void WriteSelectedLines(QuoteVersion version, EstimateOptions options)
    {
        version.Lines.Clear();
        if (!options.HasCalculatedCosts) return;
        foreach (var task in options.Tasks.OrderBy(task => task.SortOrder))
        {
            var option = task.Options.SingleOrDefault(option => option.Id == task.SelectedOptionId);
            if (option is null) continue;
            var description = $"{task.TaskType}: {option.Name}";
            version.Lines.Add(new QuoteLine
            {
                SortOrder = task.SortOrder, Description = description[..Math.Min(500, description.Length)],
                Quantity = 1, Unit = "Task", MaterialUnitCost = option.MaterialCost,
                LaborHours = 1, LaborRate = option.LaborCost, EquipmentCost = option.OtherInternalCost,
                CustomerPrice = option.CustomerPrice, Source = "Homeowner task selection"
            });
        }
    }

    private async Task AuditAsync(ApplicationDbContext db, QuoteVersion version, string explanation, string? nextStatus = null)
    {
        var quote = version.QuoteCase;
        db.QuoteAuditEvents.Add(new QuoteAuditEvent
        {
            QuoteCaseId = quote.Id, PreviousStatus = quote.Status, NewStatus = nextStatus ?? quote.Status,
            UserId = (await authentication.GetAuthenticationStateAsync()).User.FindFirstValue(ClaimTypes.NameIdentifier), Explanation = explanation
        });
        quote.Status = nextStatus ?? quote.Status;
        quote.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
