using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Pesaroll.Identity;
using Pesaroll.Organizations;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Security;
using Pesaroll.Shared.Web;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Pesaroll")
    ?? throw new InvalidOperationException("ConnectionStrings:Pesaroll is not set.");

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    // Swagger's Authorize button: paste the accessToken from sign-in or sign-up.
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "The accessToken from POST /api/auth/signin or /signup. It lasts 15 minutes; POST /api/auth/refresh gives a new one.",
    };
    document.Security ??= [];
    document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
    return Task.CompletedTask;
}));
builder.Services.AddOrganizationsModule(connectionString);
builder.Services.AddIdentityModule(connectionString, builder.Configuration);
builder.Services.AddFieldEncryption(builder.Configuration);

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
        options.EnablePersistAuthorization();       // keep the pasted token across page reloads
    });
}

// Liveness and database reachability, for uptime checks and deploys.
app.MapGet("/api/health", async (OrganizationsDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "database unreachable" }, statusCode: 503));

app.MapIdentityEndpoints();
app.MapOrganizationsEndpoints();
app.MapCompaniesEndpoints();
app.MapPaymentAccountsEndpoints();
app.MapLocationsEndpoints();
app.MapPaySchedulesEndpoints();
app.MapLocationsImportEndpoints();
app.MapStructureEndpoints();
app.MapStructureImportEndpoints();

app.Run();

// Lets the API tests start the app in memory (WebApplicationFactory).
public partial class Program;
