using BenefitCardsPortal.Data;
using BenefitCardsPortal.Security;
using BenefitCardsPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysPath);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsync("تم تجاوز عدد محاولات البحث المسموح بها. انتظر دقيقة ثم حاول مجددًا.", cancellationToken);
    };
    options.AddPolicy("CitizenSearch", context =>
    {
        var key = context.User.Identity?.IsAuthenticated == true
            ? $"user:{context.User.Identity.Name}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});
builder.Services.AddDbContext<BenefitsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("BenefitsDatabase")));
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("BenefitCardsPortal");
builder.Services.AddScoped<ICitizenSearchService, CitizenSearchService>();
builder.Services.AddScoped<IStatementOcrService, LocalStatementOcrService>();
builder.Services.AddScoped<IAuditPolicyStore, AuditPolicyStore>();
builder.Services.AddScoped<ISmtpSettingsStore, SmtpSettingsStore>();
builder.Services.AddScoped<ISmtpEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddHostedService<AuditLogCleanupService>();
builder.Services.AddHostedService<AutomaticBackupService>();
builder.Services.AddIdentity<PortalUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<BenefitsDbContext>()
    .AddDefaultTokenProviders();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<EmployeeAccountGuardMiddleware>();
app.UseAuthorization();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
await BenefitsDatabaseInitializer.InitializeAsync(app.Services, createDatabaseIfMissing: app.Environment.IsDevelopment());
await using (var scope = app.Services.CreateAsyncScope())
    await EmployeeAccountSeeder.SeedAsync(scope.ServiceProvider, builder.Configuration);

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
