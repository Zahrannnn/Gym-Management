using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Gym_Management.Auth;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Observability;
using Gym_Management.Services;
using Gym_Management.Validation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// NOTE: nothing reads configuration eagerly here. Values overridden by the integration-test
// host are merged only at Build() time, so every Jwt/ConnectionStrings/Cors read below is
// deferred into a lambda (or resolved at runtime) to see the final configuration.

builder.AddGymObservability();

// ---- Services ----
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(SwaggerDocs.Configure);

// Connection string resolved lazily so test-host overrides apply.
builder.Services.AddDbContext<GymDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IQrTokenService, QrTokenService>();
builder.Services.AddScoped<IGymClock, GymClock>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ICheckInService, CheckInService>();
builder.Services.AddScoped<AdminUserSeeder>();
builder.Services.AddScoped<IPasswordHasher<StaffUser>, PasswordHasher<StaffUser>>();

// Public portal: fixed window 10 req/min per IP (AGENTS.md).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await ProblemDetailsWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            ErrorReasons.RateLimited,
            "Too Many Requests",
            "Rate limit exceeded. Try again later.");
    };
    options.AddPolicy("public-status", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Read lazily (options are built on first use) so test-host overrides apply.
        var jwtKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = TokenService.CreateSigningKey(jwtKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = JwtBearerProblemDetails.OnChallenge,
            OnForbidden = JwtBearerProblemDetails.OnForbidden
        };
    });
builder.Services.AddAuthorization();

// CORS: the read-only Next.js customer portal origin only. Origin read lazily inside the policy.
builder.Services.AddCors(options => options.AddPolicy("Portal", policy =>
    policy.WithOrigins(builder.Configuration["Cors:PortalOrigin"] ?? "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod()));

// DataProtection keys survive app-pool recycles on shared hosting (Plesk/IIS).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(Directory.CreateDirectory(
        Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("Gym-Management");

// [ApiController] model-binding failures are 422 validation per the error contract.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value is { Errors.Count: > 0 })
            .ToDictionary(
                kvp => string.IsNullOrEmpty(kvp.Key) ? "_request" : kvp.Key,
                kvp => kvp.Value!.Errors
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                        ? (e.Exception?.Message ?? "Invalid value.")
                        : e.ErrorMessage)
                    .ToArray());

        var problem = ProblemDetailsWriter.Create(
            StatusCodes.Status422UnprocessableEntity,
            ErrorReasons.Validation,
            "Validation Failed",
            "One or more fields are invalid. See 'errors' for field-level messages.",
            context.HttpContext.Request.Path);
        problem.Extensions["errors"] = errors;
        return new UnprocessableEntityObjectResult(problem);
    };
});

var app = builder.Build();

// Fail fast if the signing key is unusable (checked after Build so the final, merged
// configuration — including environment overrides — is what gets validated).
var jwtKey = app.Configuration["Jwt:Key"] ?? string.Empty;
if (Encoding.UTF8.GetByteCount(jwtKey) < TokenService.MinimumKeyLength)
{
    throw new InvalidOperationException(
        $"Jwt:Key must be configured with at least {TokenService.MinimumKeyLength} characters.");
}

// ---- Startup: apply migrations, then idempotent seeds (rule: Migrate() at startup — no shell on Plesk/IIS) ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GymDbContext>();
    await db.Database.MigrateAsync();

    await scope.ServiceProvider.GetRequiredService<ISettingsService>().InitializeAsync();
    await scope.ServiceProvider.GetRequiredService<AdminUserSeeder>().SeedAsync();
}

// ---- Pipeline ----
app.UseGymObservability();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseCors("Portal");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Logger.LogInformation(
    GymLogEvents.StartupReady,
    "Gym Management API ready environment={Environment} contentRoot={ContentRoot}",
    app.Environment.EnvironmentName,
    app.Environment.ContentRootPath);

app.Run();

/// <summary>Exposes Program to Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program { }
