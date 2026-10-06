using Microsoft.AspNetCore.HttpOverrides;
using Pesaroll.Identity;
using Pesaroll.Organizations;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Web;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Pesaroll")
    ?? throw new InvalidOperationException("ConnectionStrings:Pesaroll is not set.");

builder.Services.AddOpenApi();
builder.Services.AddOrganizationsModule(connectionString);
builder.Services.AddIdentityModule(connectionString, builder.Configuration);

// The API only runs behind Caddy, which sets the client's address and scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ErrorResponseMiddleware>();
app.UseMiddleware<OriginCheckMiddleware>();
app.UseRateLimiter();
app.UseSessions();

// The API description and a page to try it: development only, never published in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                       // /openapi/v1.json (also the source of the web app's types)
    app.UseSwaggerUI(options =>             // /swagger
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Pesaroll API");
        options.RoutePrefix = "swagger";
        options.EnableTryItOutByDefault();
    });
}

// Liveness and database reachability, for uptime checks and deploys.
app.MapGet("/api/health", async (OrganizationsDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "database unreachable" }, statusCode: 503));

app.MapIdentityEndpoints();

app.Run();

// Lets the API tests start the app in memory (WebApplicationFactory).
public partial class Program;
