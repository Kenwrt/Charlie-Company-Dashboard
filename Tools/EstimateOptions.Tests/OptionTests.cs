using System.Reflection;
using System.Text.Json;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;

public class OptionTests
{
    [Fact] public void LegacyPriceStillUsesSavedLines()
    {
        var version = new QuoteVersion { DiscountAmount = 10, TaxRate = 10,
            Lines = [new QuoteLine { CustomerPrice = 100 }, new QuoteLine { CustomerPrice = 50 }] };
        Assert.Equal(150m, version.Subtotal);
        Assert.Equal(154m, version.Total);
    }

    [Fact] public void OnlyOneSelectedOptionPerTaskContributesToTotal()
    {
        var a = new EstimateOption { CustomerPrice = 100, IsReady = true };
        var b = new EstimateOption { CustomerPrice = 900, IsReady = true };
        var c = new EstimateOption { CustomerPrice = 50, IsReady = true };
        var doc = new EstimateOptions { Tasks = [
            new() { Options = [a,b], SelectedOptionId = a.Id },
            new() { Options = [c], SelectedOptionId = c.Id }] };
        Assert.True(doc.IsComplete);
        var version = new QuoteVersion { OptionsJson = doc.Write(), TaxRate = 10, DiscountAmount = 10 };
        Assert.Equal(154m, version.Total);
        doc.Tasks[0].SelectedOptionId = b.Id;
        Assert.Equal(950m, doc.CustomerPrice);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RequiredTasksNeedASelection(bool required, bool complete)
    {
        var doc = new EstimateOptions { Tasks = [new() { IsRequired = required, Options = [new()] }] };
        Assert.Equal(complete, doc.IsComplete);
    }

    [Fact] public void ApprovedSnapshotDoesNotFollowChangedTaskMeasurements()
    {
        var task = new QuoteProjectTask { Id = 1, Measurements = "changed" };
        var doc = new EstimateOptions { Tasks = [new() { TaskId = 1, Measurements = "original",
            Options = [new() { CustomerPrice = 125, IsReady = true }] }] };
        var version = new QuoteVersion { Status = "Approved", OptionsJson = doc.Write(), QuoteCase = new() { ProjectTasks = [task] } };
        Assert.Equal("original", EstimateOptionsService.ForEditing(version).Tasks[0].Measurements);
        Assert.Equal(125m, EstimateOptionsService.ForEditing(version).Tasks[0].Options[0].CustomerPrice);
    }

    [Fact] public void DraftMeasurementChangeInvalidatesEveryOption()
    {
        var a = new EstimateOption { IsReady = true };
        var doc = new EstimateOptions { Tasks = [new() { TaskId = 1, Measurements = "old", SelectedOptionId = a.Id, Options = [a, new() { IsReady = true }] }] };
        var version = new QuoteVersion { Status = "Draft", OptionsJson = doc.Write(),
            QuoteCase = new() { ProjectTasks = [new() { Id = 1, Measurements = "new" }] } };
        var edited = EstimateOptionsService.ForEditing(version).Tasks[0];
        Assert.Null(edited.SelectedOptionId);
        Assert.All(edited.Options, option => Assert.False(option.IsReady));
    }

    [Fact] public void CopyJsonIsIndependentAndChangesInvalidatePricing()
    {
        var source = new EstimateOption { EstimatedDays = 2, CrewSize = 3, DailyCostPerCrewMember = 200,
            Materials = [new() { Description = "wood decking", UnitCost = 12 }] };
        var copy = JsonSerializer.Deserialize<EstimateOption>(JsonSerializer.Serialize(source))!;
        copy.Materials[0].UnitCost = 20;
        Assert.Equal(12m, source.Materials[0].UnitCost);
        Assert.Equal(600m, copy.DailyCrewCost);
        copy.PricingInputsSignature = copy.CalculationSignature();
        Assert.True(copy.IsCalculationCurrent);
        copy.CrewSize = 4;
        Assert.False(copy.IsCalculationCurrent);
    }

    [Theory]
    [InlineData(EstimateOptionSubstitutions.Trex, "Trex composite decking")]
    [InlineData(EstimateOptionSubstitutions.Deckorators, "Deckorators composite decking")]
    public void SubstitutionReplacesWoodAndPreservesFraming(string key, string replacement)
    {
        var option = Copied(key);
        var retained = Reconcile(option, Response(key, replacement));
        Assert.Single(retained);
        Assert.Equal("treated joist", retained[0].Description);
        Assert.Equal(50m, retained[0].UnitCost);
        Assert.Equal("wood decking", option.CopySource!.Materials[0].Description);
    }

    [Theory]
    [InlineData("wood decking")]
    [InlineData("Generic composite decking")]
    public void WrongReplacementIsRejected(string replacement)
        => Assert.Throws<InvalidOperationException>(() => Reconcile(Copied(EstimateOptionSubstitutions.Trex), Response(EstimateOptionSubstitutions.Trex, replacement)));

    [Fact] public void IncompleteLedgerIsRejected()
        => Assert.Throws<InvalidOperationException>(() => Reconcile(Copied(EstimateOptionSubstitutions.Trex), "{\"materials\":[],\"sourceMaterialActions\":[]}"));

    [Fact] public void StructuralRemovalIsRejected()
    {
        var json = Response(EstimateOptionSubstitutions.Trex, "Trex decking").Replace("\"action\":\"keep\"", "\"action\":\"remove\",\"substitutionKey\":\"wood-decking-to-trex\",\"reason\":\"remove frame\"");
        Assert.Throws<InvalidOperationException>(() => Reconcile(Copied(EstimateOptionSubstitutions.Trex), json));
    }

    [Theory]
    [InlineData("treated joist")]
    [InlineData("wood support post")]
    [InlineData("Trex decking")]
    public void IneligibleSourceDoesNotOfferWoodReplacement(string description)
        => Assert.False(EstimateOptionSubstitutions.CanApply(new() { Materials = [new() { Description = description }] }, EstimateOptionSubstitutions.Trex));

    [Fact] public void MigrationSnapshotMatchesCurrentModelWithoutDatabase()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql("Host=localhost;Database=unused"));
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20260922235900_AddTaskEstimateOptions", "20260922235900_AddTaskEstimateOptions");
        Assert.DoesNotContain("DROP TABLE", sql);
    }

    static EstimateOption Copied(string key) => new() { Substitutions = [key], CopySource = new() {
        Materials = [new() { Description = "wood decking", UnitCost = 10 }, new() { Description = "treated joist", UnitCost = 50 }] } };

    static string Response(string key, string replacement) => JsonSerializer.Serialize(new {
        sourceMaterialActions = new object[] {
            new { sourceLineNumber = 1, action = "replace", substitutionKey = key, reason = "requested replacement" },
            new { sourceLineNumber = 2, action = "keep" } },
        materials = new[] { new { description = replacement, quantity = 10, unit = "Each", substitutionKey = key, replacesSourceLines = new[] { 1 } } }
    });

    static IReadOnlyList<EstimateOptionMaterial> Reconcile(EstimateOption option, string json)
    {
        var type = typeof(CentComTaskAnalysisService);
        var result = type.GetMethod("ParseResponse", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [json]);
        try { return (IReadOnlyList<EstimateOptionMaterial>)type.GetMethod("ReconcileCopiedMaterials", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [option, result])!; }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException failure) { throw failure; }
    }
}
