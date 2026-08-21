using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ModlistManager.Components;
using ModlistManager.Data;
using ModlistManager.Services;

var builder = WebApplication.CreateBuilder(args);

// Data
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=modlist.db";
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

// Persist Data Protection keys (used to sign/encrypt the auth cookie and antiforgery tokens) outside
// the container filesystem, so they survive restarts instead of silently invalidating existing
// sessions/tokens on every redeploy.
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}

// App services
builder.Services.Configure<SteamCmdOptions>(builder.Configuration.GetSection(SteamCmdOptions.SectionName));
builder.Services.AddSingleton<SteamCmdFetchQueue>();
builder.Services.AddHostedService<SteamCmdFetchService>();
builder.Services.AddSingleton<ModRequestService>();
builder.Services.AddSingleton<AdminAuthService>();

// Auth: single admin account behind a cookie, no self-registration.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Apply migrations and seed the single admin credential from the startup password.
using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbContextFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();

    var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? builder.Configuration["AdminPassword"];
    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        throw new InvalidOperationException(
            "No admin password configured. Set the ADMIN_PASSWORD environment variable (or an AdminPassword " +
            "config value) before starting the app.");
    }

    var authService = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
    await authService.SeedAsync(adminPassword);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Plain (non-Blazor) endpoints for sign-in/out: these need direct HttpContext access to set the
// auth cookie, which an interactive Razor component circuit cannot do after the initial response.
app.MapPost("/account/login", async (HttpContext ctx, AdminAuthService auth) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var password = form["password"].ToString();

    if (await auth.VerifyPasswordAsync(password))
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin")],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.LocalRedirect("/admin");
    }

    return Results.LocalRedirect("/login?error=1");
});

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
});

// Plain (non-Blazor) endpoint for submitting a mod request: EditForm/OnValidSubmit renders a real
// <form> as a progressive-enhancement fallback for when the interactive circuit isn't connected yet,
// but nothing was wired up to handle that fallback POST - it just 400'd on antiforgery validation.
// A plain form + minimal API endpoint (matching the login pattern above) sidesteps that entirely.
app.MapPost("/requests", async (HttpContext ctx, ModRequestService requestService) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var result = await requestService.CreateRequestAsync(form["title"], form["workshopInput"], form["requesterName"]);

    return result.Success
        ? Results.LocalRedirect("/")
        : Results.LocalRedirect($"/?error={Uri.EscapeDataString(result.Error ?? "Could not create the request.")}");
});

app.Run();
