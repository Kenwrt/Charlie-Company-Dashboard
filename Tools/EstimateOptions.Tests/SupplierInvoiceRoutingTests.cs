using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Options;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class SupplierInvoiceRoutingTests
{
    private static readonly LocalOperation[] Locations = [
        new() { Id = 11, Slug = "nashville", IsActive = true },
        new() { Id = 22, Slug = "murfreesboro", IsActive = true },
        new() { Id = 33, Slug = "knoxville", IsActive = true }];

    private static SupplierInvoiceRouting Router(bool multiple = false)
    {
        var settings = new SupplierInvoiceOptions();
        if (multiple) settings.ParticipatingLocationSlugs = ["nashville", "murfreesboro", "knoxville"];
        return new(Options.Create(settings));
    }

    [Theory]
    [InlineData("")]
    [InlineData("John Smith")]
    public void LegacyCustomerReferencesDefaultToNashvilleOnlyInSingleLocationMode(string reference)
    {
        var result = Router().Resolve(reference, Locations);
        Assert.Equal(11, result.OperationId);
        Assert.False(result.RequiresReview);
        Assert.True(Router(true).Resolve(reference, Locations).RequiresReview);
    }

    [Theory]
    [InlineData("[n-s-h] | Job 1042 | Smith", 11)]
    [InlineData("Murfreesboro | Job 1042 | Smith", 22)]
    [InlineData("KNX1042 Smith", 33)]
    [InlineData("knoxville | Smith", 33)]
    public void ExplicitLocationIsIndependentOfSharedJobNumber(string reference, int expected)
    {
        var result = Router(true).Resolve(reference, Locations);
        Assert.Equal(expected, result.OperationId);
        Assert.False(result.RequiresReview);
    }

    [Theory]
    [InlineData("Nashvile | Smith")]
    [InlineData("KXN | Smith")]
    [InlineData("NHS | Smith")]
    [InlineData("NSH | KNX | Smith")]
    [InlineData("LOC=XYZ | Smith")]
    public void AmbiguityAndTyposNeverUseTheNashvilleFallback(string reference)
    {
        var result = Router().Resolve(reference, Locations);
        Assert.Null(result.OperationId);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public void KnownButNotParticipatingLocationNeedsReview() =>
        Assert.True(Router().Resolve("MUR | Smith", Locations).RequiresReview);

    [Fact]
    public void InactiveLocationCannotBeAssigned() =>
        Assert.True(Router(true).Resolve("KNX | Smith", Locations.Where(x => x.Id != 33).ToArray()).RequiresReview);
}
