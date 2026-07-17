using CharleyCompany.Dashboard.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public static class IdentityBootstrapService
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in ApplicationRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSucceeded(result, $"creating role {role}");
            }
        }

        var seededOperations = new[]
        {
            new LocalOperation { Name = "Charlie Company Nashville", Slug = "nashville" },
            new LocalOperation { Name = "Charlie Company Knoxville", Slug = "knoxville" },
            new LocalOperation { Name = "Charlie Company Chattanooga", Slug = "chattanooga" }
        };

        foreach (var operation in seededOperations)
        {
            if (!await dbContext.LocalOperations.AnyAsync(existing => existing.Slug == operation.Slug))
            {
                dbContext.LocalOperations.Add(operation);
            }
        }

        await dbContext.SaveChangesAsync();

        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var administrator = await userManager.FindByEmailAsync(email);
        if (administrator is null)
        {
            administrator = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            EnsureSucceeded(await userManager.CreateAsync(administrator, password), "creating bootstrap administrator");
        }

        if (!await userManager.IsInRoleAsync(administrator, ApplicationRoles.Administrator))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(administrator, ApplicationRoles.Administrator), "assigning administrator role");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Failed {action}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
    }
}
