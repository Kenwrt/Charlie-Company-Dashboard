using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CharleyCompany.Dashboard.Web.Components;
using CharleyCompany.Dashboard.Web.Components.Account;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Endpoints;
using CharleyCompany.Dashboard.Web.Options;
using CharleyCompany.Dashboard.Web.Services;
using Serilog;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionKeysPath = builder.Environment.IsProduction()
        ? "/var/lib/charlie-company/dataprotection"
        : Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys");
}

var logFilePath = builder.Configuration["Logging:FilePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "Logs", "charley-dashboard-.log");

try
{
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.Console()
        .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Hour, retainedFileCountLimit: 168, shared: true));

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityRedirectManager>();
    builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName("CharlieCompanyDashboard");

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddIdentityCookies();

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
    builder.Services.Configure<HousecallProOptions>(builder.Configuration.GetSection(HousecallProOptions.SectionName));
    builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
    builder.Services.AddSingleton<DashboardNotificationService>();
    builder.Services.AddScoped<MockDashboardDataSource>();
    builder.Services.AddScoped<OperationAccessService>();
    builder.Services.AddScoped<OperationCatalogService>();
    builder.Services.AddSingleton<OperationalEventBroker>();
    builder.Services.AddScoped<OperationalEventPublisher>();
    builder.Services.AddHostedService<OperationalEventRetentionService>();
    builder.Services.AddHttpClient<RemoteOperationalEventForwarder>();
    builder.Services.AddHostedService(services => services.GetRequiredService<RemoteOperationalEventForwarder>());
    builder.Services.AddRateLimiter(options => options.AddPolicy("observability-ingestion", context => RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new TokenBucketRateLimiterOptions { TokenLimit = 120, TokensPerPeriod = 120, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 20 })));
    builder.Services.AddScoped<IFinanceDataSource, SpreadsheetFinanceDataSource>();
    builder.Services.AddScoped<IOutboundNotificationSender, EmailNotificationSender>();
    builder.Services.AddScoped<IOutboundNotificationSender, MobileNotificationSender>();
    builder.Services.AddScoped<WebhookNotificationDispatcher>();
    builder.Services.AddHttpClient<HousecallProDashboardDataSource>();
    builder.Services.AddScoped<IDashboardDataSource>(services => services.GetRequiredService<HousecallProDashboardDataSource>());
    builder.Services.AddHostedService<HousecallProSyncService>();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.Migrate();
        await IdentityBootstrapService.InitializeAsync(scope.ServiceProvider, builder.Configuration);
    }

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseSerilogRequestLogging();
    app.UseAntiforgery();
    app.UseRateLimiter();

    app.MapStaticAssets();
    app.MapHealthEndpoints();
    app.MapHousecallProWebhookEndpoints();
    app.MapObservabilityEndpoints();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Add additional endpoints required by the Identity /Account Razor components.
    app.MapAdditionalIdentityEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Charley Company Dashboard terminated unexpectedly.");
    Console.Error.WriteLine(ex);
}
finally
{
    Log.CloseAndFlush();
}
