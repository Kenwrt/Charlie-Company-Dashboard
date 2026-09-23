using System.Text.Json;
using System.Text.Json.Serialization;

namespace CharleyCompany.Dashboard.Web.Data;

// A version owns its offered prices and selections. Task measurements are captured
// once per task when saved, so later revisions cannot change an approved offer.
public sealed class EstimateOptions
{
    public Guid RevisionId { get; set; } = Guid.NewGuid();
    public List<EstimateTaskOptions> Tasks { get; set; } = [];
    public static EstimateOptions Read(string json) =>
        JsonSerializer.Deserialize<EstimateOptions>(json)
        ?? throw new InvalidOperationException("The saved estimate options could not be read.");
    public string Write()
    {
        RevisionId = Guid.NewGuid();
        return JsonSerializer.Serialize(this);
    }
    public static string AnalysisSignature(QuoteProjectTask task, EstimateOption option)
    {
        var input = JsonSerializer.Serialize(new { task.TaskType, WorkType = option.HasPlanningInputs ? option.WorkType : option.WorkType ?? task.WorkType,
            task.ScopeOfWork, task.Measurements, Overview = task.QuoteCase.WorkDescription, option.Name, option.Description });
        // Keep signatures of older, non-copied options unchanged.
        if (option.CopySource is not null || option.Substitutions.Count > 0)
            input = JsonSerializer.Serialize(new { Input = input, option.CopySource, Substitutions = option.Substitutions.OrderBy(key => key).ToArray() });
        if (option.CrewCount is not null)
            input = JsonSerializer.Serialize(new { Input = input, option.CrewCount, option.EstimatedDays });
        if (option.AutomaticMaterial is not null)
            input = JsonSerializer.Serialize(new { Input = input, option.AutomaticMaterial, option.IncludeRailing,
                Photos = task.Photos.OrderBy(photo => photo.Id).Select(photo => photo.Id).ToArray() });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)));
    }
    [JsonIgnore] public IEnumerable<EstimateOption> Selected => Tasks
        .Select(task => task.Options.SingleOrDefault(option => option.Id == task.SelectedOptionId))
        .OfType<EstimateOption>();
    [JsonIgnore] public bool HasCalculatedCosts => Tasks.Count > 0 && Selected.Any() && Tasks.All(task =>
        task.Options.Any(option => option.Id == task.SelectedOptionId
            && option.IsReady && (!option.HasPlanningInputs || option.IsCalculationCurrent)));
    [JsonIgnore] public bool IsComplete => Tasks.Count > 0 && Tasks.All(task =>
        (!task.IsRequired && task.SelectedOptionId is null)
        || task.Options.Any(option => option.Id == task.SelectedOptionId && option.IsReady));
    [JsonIgnore] public decimal CustomerPrice => Selected.Sum(option => option.CustomerPrice);
    [JsonIgnore] public decimal InternalCost => Selected.Sum(option => option.InternalCost);
    [JsonIgnore] public decimal MarketValue => Selected.Sum(option => option.MarketValue);
}

public sealed class EstimateTaskOptions
{
    public int TaskId { get; set; }
    public int SortOrder { get; set; }
    public string TaskType { get; set; } = "";
    public string ScopeOfWork { get; set; } = "";
    public string Measurements { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public Guid? SelectedOptionId { get; set; }
    public Guid? PriceOptionId { get; set; }
    public List<EstimateOption> Options { get; set; } = [];
}

public sealed class EstimateOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Standard";
    public string Description { get; set; } = "";
    public string? AutomaticMaterial { get; set; }
    public bool IncludeRailing { get; set; }
    public List<EstimateOptionMaterial> Materials { get; set; } = [];
    public EstimateOptionCopySource? CopySource { get; set; }
    public List<string> Substitutions { get; set; } = [];
    // Nullable planning inputs preserve older JSON and accepted price snapshots.
    public string? WorkType { get; set; }
    public decimal? EstimatedDays { get; set; }
    public decimal? CrewSize { get; set; }
    public int? CrewCount { get; set; }
    public decimal? DailyCostPerCrewMember { get; set; }
    public decimal? TargetMarginPercent { get; set; }
    public decimal AdditionalBaselineCost { get; set; }
    public string? PricingInputsSignature { get; set; }
    public decimal LaborCost { get; set; }
    public decimal OtherInternalCost { get; set; }
    public decimal MarketValue { get; set; }
    public decimal CustomerPrice { get; set; }
    public bool IsReady { get; set; }
    public bool IsProvisionalPrice { get; set; }
    public bool HasUnpricedMaterials { get; set; }
    public string? PricingSourceSignature { get; set; }
    public bool RequiresCentComAnalysis { get; set; }
    public int? SourceAnalysisId { get; set; }
    public string? CostBasis { get; set; }
    [JsonIgnore] public decimal? DailyCrewCost => CrewSize is null || DailyCostPerCrewMember is null
        ? null : decimal.Round(CrewSize.Value * DailyCostPerCrewMember.Value, 2);
    [JsonIgnore] public decimal BaselineCost => MaterialCost + LaborCost + AdditionalBaselineCost;
    [JsonIgnore] public bool HasPlanningInputs => EstimatedDays is not null || CrewSize is not null
        || DailyCostPerCrewMember is not null || TargetMarginPercent is not null;
    [JsonIgnore] public bool IsCalculationCurrent => PricingInputsSignature == CalculationSignature();
    public decimal PriceIncludingTax(decimal taxRate) => CustomerPrice + decimal.Round(CustomerPrice * taxRate / 100m, 2);
    public string CalculationSignature()
    {
        var input = JsonSerializer.Serialize(new
        {
            WorkType, EstimatedDays, CrewSize, DailyCostPerCrewMember, TargetMarginPercent,
            AdditionalBaselineCost, Materials, LaborCost, OtherInternalCost
        });
        if (CrewCount is not null) input = JsonSerializer.Serialize(new { Input = input, CrewCount });
        if (CopySource is not null || Substitutions.Count > 0)
            input = JsonSerializer.Serialize(new { Input = input, CopySource, Substitutions = Substitutions.OrderBy(key => key).ToArray() });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)));
    }
    [JsonIgnore] public decimal MaterialCost => Materials.Sum(material => material.ExtendedCost);
    [JsonIgnore] public decimal InternalCost => MaterialCost + LaborCost + OtherInternalCost;
}

public sealed class EstimateOptionMaterial
{
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "Each";
    public string VendorName { get; set; } = "Manual materials";
    public string? VendorSku { get; set; }
    public bool IsPolicySupply { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public decimal WastePercent { get; set; }
    [JsonIgnore] public decimal ExtendedCost => decimal.Round(Quantity * UnitCost * (1 + WastePercent / 100m), 2);
}
