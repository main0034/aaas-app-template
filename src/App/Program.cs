// Template application.
//
// Deliberately small, but complete: it starts, serves /health without a database,
// and exercises Postgres on the endpoints that need it. That shape matters - the
// container must become healthy even when the database is unreachable, otherwise
// a database problem looks like a deployment failure and you debug the wrong thing.
//
// The same binary applies schema migrations when started with the single
// argument `migrate`. The platform runs that as an init container before each new
// revision starts. See AGENT.md.
//
// Agents extending this file: read AGENT.md first.

using System.ComponentModel.DataAnnotations;
using App;
using App.Data;
using Microsoft.EntityFrameworkCore;

var migrateOnly = args is ["migrate"];

var builder = WebApplication.CreateBuilder(args);

// The platform injects PORT. Kestrel's own default (8080) is not the contract.
var port = builder.Configuration["PORT"] ?? "8000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var appName = builder.Configuration["APP_NAME"] ?? "app";

// EF logs every SQL statement at Information, which buries the lines that matter.
// In migrate mode it is silenced entirely: when replicas race, EF's own queries
// fail harmlessly while another replica holds the lock, and a "fail:" line in an
// init container log reads like the cause of whatever goes wrong next. The runner
// reports real failures itself.
builder.Logging.AddFilter(
    "Microsoft.EntityFrameworkCore.Database.Command",
    migrateOnly ? LogLevel.None : LogLevel.Warning);

builder.Services.AddSingleton<Database>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var db = sp.GetRequiredService<Database>();
    if (db.IsConfigured)
    {
        options.UseNpgsql(db.DataSource);
    }
    else
    {
        options.UseNpgsql().AddInterceptors(new NotConfiguredInterceptor());
    }
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseUnavailableHandler>();
builder.Services.AddValidation();

var app = builder.Build();

if (migrateOnly)
{
    return await MigrationRunner.ApplyAsync(app.Services);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Liveness and readiness. Must never touch the database.
app.MapGet("/health", () => Results.Ok(new { status = "ok", app = appName }));

// Reports database connectivity. Not wired to the container probes.
app.MapGet("/ready", async (Database db, IServiceProvider sp, CancellationToken ct) =>
{
    if (!db.IsConfigured)
    {
        return Results.Ok(new { database = "unconfigured" });
    }

    try
    {
        var ctx = sp.GetRequiredService<AppDbContext>();
        var applied = await ctx.Database.GetAppliedMigrationsAsync(ct);
        return Results.Ok(new
        {
            database = "ok",
            auth = db.UsesManagedIdentity ? "managed-identity" : "password",
            migration = applied.LastOrDefault(),
        });
    }
    catch (Exception ex) when (DatabaseUnavailableHandler.IsDatabaseFailure(ex))
    {
        return Results.Ok(new { database = "unavailable", detail = ex.GetBaseException().Message });
    }
});

app.MapGet("/items", async (AppDbContext ctx, CancellationToken ct) =>
    await ctx.Items.AsNoTracking().OrderByDescending(i => i.Id).Take(100).ToListAsync(ct));

app.MapPost("/items", async (ItemIn input, AppDbContext ctx, CancellationToken ct) =>
{
    var item = new Item { Title = input.Title, Note = input.Note };
    ctx.Items.Add(item);
    await ctx.SaveChangesAsync(ct);
    return Results.Created($"/items/{item.Id}", item);
});

await app.RunAsync();
return 0;

public sealed record ItemIn(
    [property: Required, StringLength(200, MinimumLength = 1)] string Title,
    [property: StringLength(2000)] string? Note);

// Exposes Program to WebApplicationFactory in the tests.
public partial class Program;
