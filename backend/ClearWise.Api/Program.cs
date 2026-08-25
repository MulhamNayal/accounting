using System.Text;
using ClearWise.Api.Auth;
using ClearWise.Api.Data;
using ClearWise.Api.Middleware;
using ClearWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

const string DevCorsPolicy = "DevFrontend";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy =>
    policy.WithOrigins("http://localhost:5173")
          .AllowAnyHeader()
          .AllowAnyMethod()));

// ---------------------------------------------------------------- authentication

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// Fail at startup rather than serve forgeable tokens. There is deliberately no fallback
// key: a default would be identical on every installation, which is the same as none.
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < JwtOptions.MinimumKeyBytes)
{
    throw new InvalidOperationException(
        $"Jwt:SigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes. Set it with: "
        + "dotnet user-secrets set \"Jwt:SigningKey\" \"<a long random string>\"");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            // The default five minutes of slack is generous for a one-hour token.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Authentication is the default, not something each endpoint opts into. A new controller
    // added without an attribute is protected rather than public, which is the direction a
    // mistake should fall in an application holding other people's books.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ---------------------------------------------------------------- services

// Scoped: the tenant is resolved per request, and the interceptor reads it whenever a
// connection opens.
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<TenantConnectionInterceptor>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEntityService, EntityService>();
builder.Services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<INumberSeriesService, NumberSeriesService>();
builder.Services.AddScoped<IPostingService, PostingService>();
builder.Services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
builder.Services.AddScoped<IReceivablesService, ReceivablesService>();
builder.Services.AddScoped<ITaxService, TaxService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<IConsolidationService, ConsolidationService>();

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

app.UseAuthentication();
// Order matters: the tenant is read from the authenticated principal's claims, so this must
// run after authentication has populated it and before anything opens a connection.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can drive the real HTTP pipeline.</summary>
public partial class Program;
