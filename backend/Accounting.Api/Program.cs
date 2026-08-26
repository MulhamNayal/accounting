using System.Text;
using Accounting.Api.Auth;
using Accounting.Api.Data;
using Accounting.Api.Middleware;
using Accounting.Api.Services;
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
builder.Services.AddScoped<IFinancialStatementsService, FinancialStatementsService>();
builder.Services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
builder.Services.AddScoped<IReceivablesService, ReceivablesService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IPurchaseInvoiceService, PurchaseInvoiceService>();
builder.Services.AddScoped<IPayablesService, PayablesService>();
builder.Services.AddScoped<ISalesCreditNoteService, SalesCreditNoteService>();
builder.Services.AddScoped<IPurchaseCreditNoteService, PurchaseCreditNoteService>();
builder.Services.AddScoped<ITaxService, TaxService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<IConsolidationService, ConsolidationService>();

// Posting rules are stateless pure functions from document to posting set.
builder.Services.AddSingleton<SalesInvoicePostingRule>();
builder.Services.AddSingleton<PurchaseInvoicePostingRule>();
builder.Services.AddSingleton<SalesCreditNotePostingRule>();
builder.Services.AddSingleton<PurchaseCreditNotePostingRule>();

// Controllers never catch. Exception type maps to status code in one place.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AccountingDbContext>((serviceProvider, options) =>
{
    // The runtime connection uses the low-privilege application role, which cannot run DDL
    // and - from Layer 1 onward - cannot UPDATE or DELETE ledger rows. Migrations connect
    // as the owner role instead, via AccountingDbContextFactory.
    var connectionString = builder.Configuration.GetConnectionString("AccountingDatabase")
        ?? throw new InvalidOperationException(
            "Connection string 'AccountingDatabase' is not configured. Set it with: "
            + "dotnet user-secrets set \"ConnectionStrings:AccountingDatabase\" \"...\"");

    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention()
           .AddInterceptors(serviceProvider.GetRequiredService<TenantConnectionInterceptor>());
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // AllowAnonymous because the fallback policy below requires an authenticated user for
    // every endpoint, and an API description you need a token to read is no use for
    // exploring the API.
    app.MapOpenApi().AllowAnonymous();
    app.UseCors(DevCorsPolicy);
}

// Migrations are NOT applied at startup - they run as the owner role, ahead of deploy.
// Only demonstration data is seeded here.
//
// Gated on the password rather than on the environment: a deployed instance needs a way in
// too, and this repository is public, so the credential has to come from configuration. No
// password configured means no seeding, which is the right default for an instance that is
// meant to hold real books.
var seedPassword = builder.Configuration["Seed:DemoPassword"];
if (!string.IsNullOrWhiteSpace(seedPassword))
{
    await DevDataSeeder.SeedAsync(app.Services, seedPassword);
}

app.UseHttpsRedirection();

// The SPA is published into wwwroot and served by this app, so the whole product is one
// origin and one IIS application. UseDefaultFiles maps "/" to index.html; MapFallbackToFile
// below hands client-side routes to it as well.
app.UseDefaultFiles();
app.UseStaticFiles();

// Explicit, and deliberately AFTER the static file middleware. WebApplication otherwise
// auto-inserts UseRouting at the very top of the pipeline, which selects the fallback
// endpoint before the static files ever run - and both UseDefaultFiles and UseStaticFiles
// stand down once an endpoint has been selected. The result is that "/" bypasses index.html
// entirely and lands on the authenticated fallback endpoint as a 401.
app.UseRouting();

app.UseAuthentication();
// Order matters: the tenant is read from the authenticated principal's claims, so this must
// run after authentication has populated it and before anything opens a connection.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

// An unmatched /api route has to stay a 404. Without this the fallback below would answer
// it with index.html, and the client would report a JSON parse error instead of the 404 the
// server actually meant. A catch-all parameter is the least specific route there is, so
// every real controller action still wins.
app.Map("/api/{**rest}", () => Results.NotFound());

// Everything else that isn't a real file is a client-side route. AllowAnonymous because this
// serves the application shell, not data: a deep link like /invoices has to return the page
// so the SPA can render its sign-in screen. Every API call the page then makes is still
// subject to the fallback policy.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

/// <summary>Exposed so integration tests can drive the real HTTP pipeline.</summary>
public partial class Program;
