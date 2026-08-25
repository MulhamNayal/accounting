using ClearWise.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Scoped: the tenant is resolved per request, and the interceptor reads it whenever a
// connection opens.
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantConnectionInterceptor>();

builder.Services.AddDbContext<ClearWiseDbContext>((serviceProvider, options) =>
{
    // The runtime connection uses the low-privilege application role, which cannot run DDL
    // and - from Layer 1 onward - cannot UPDATE or DELETE ledger rows. Migrations connect
    // as the owner role instead, via ClearWiseDbContextFactory.
    var connectionString = builder.Configuration.GetConnectionString("ClearWiseDatabase")
        ?? throw new InvalidOperationException(
            "Connection string 'ClearWiseDatabase' is not configured. Set it with: "
            + "dotnet user-secrets set \"ConnectionStrings:ClearWiseDatabase\" \"...\"");

    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention()
           .AddInterceptors(serviceProvider.GetRequiredService<TenantConnectionInterceptor>());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can drive the real HTTP pipeline.</summary>
public partial class Program;
