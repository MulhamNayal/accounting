using ClearWise.Api.Data;
using ClearWise.Api.Middleware;
using ClearWise.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

const string DevCorsPolicy = "DevFrontend";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy =>
    policy.WithOrigins("http://localhost:5173")
          .AllowAnyHeader()
          .AllowAnyMethod()));

// Scoped: the tenant is resolved per request, and the interceptor reads it whenever a
// connection opens.
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<TenantConnectionInterceptor>();
builder.Services.AddScoped<IEntityService, EntityService>();
builder.Services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<INumberSeriesService, NumberSeriesService>();
builder.Services.AddScoped<IPostingService, PostingService>();
builder.Services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
builder.Services.AddScoped<IReceivablesService, ReceivablesService>();
builder.Services.AddScoped<ITaxService, TaxService>();

// Posting rules are stateless pure functions from document to posting set.
builder.Services.AddSingleton<SalesInvoicePostingRule>();

// Controllers never catch. Exception type maps to status code in one place.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

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

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(DevCorsPolicy);

    // Migrations are NOT applied at startup - they run as the owner role, ahead of deploy.
    // Only demonstration data is seeded here, and only in Development.
    await DevDataSeeder.SeedAsync(app.Services);
}

app.UseHttpsRedirection();
app.UseMiddleware<TenantResolutionMiddleware>();
app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can drive the real HTTP pipeline.</summary>
public partial class Program;
