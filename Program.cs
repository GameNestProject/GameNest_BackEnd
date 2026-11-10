using GameNest_BackEnd.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Microsoft.OpenApi;
using GameNest_BackEnd.Services;
using GameNest_BackEnd.Cli;

// Operator command (no web server): dotnet run --no-launch-profile -- admin promote --email <email>
// Its arguments are not passed to the configuration system.
var isAdminCommand = AdminCommand.IsAdminCommand(args);
var builder = WebApplication.CreateBuilder(isAdminCommand ? [] : args);
if (isAdminCommand)
{
    Environment.ExitCode = await AdminCommand.RunAsync(args, builder.Configuration, Console.In, Console.Out, Console.Error);
    return;
}

// Secrets are never stored in appsettings.json: use User Secrets in Development
// and environment variables (e.g. Jwt__Key, ConnectionStrings__DefaultConnection) in Production.
static string RequireSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException(
            $"{key} must be configured (User Secrets in Development, environment variables in Production).");
    return value;
}

var jwtKey = RequireSetting(builder.Configuration, "Jwt:Key");
// HMAC-SHA256 requires a key of at least 256 bits.
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes (256 bits) long.");
var jwtIssuer = RequireSetting(builder.Configuration, "Jwt:Issuer");
var jwtAudience = RequireSetting(builder.Configuration, "Jwt:Audience");
var connectionString = RequireSetting(builder.Configuration, "ConnectionStrings:DefaultConnection");

// Google sign-in is the public way to register and log in, so its client ID is required outside Development.
var googleClientId = builder.Configuration["Google:ClientId"];
if (!string.IsNullOrWhiteSpace(googleClientId) || !builder.Environment.IsDevelopment())
{
    if (!GoogleAuthOptions.IsValidClientId(googleClientId))
        throw new InvalidOperationException(
            $"Google:ClientId (environment variable Google__ClientId) must be set to an OAuth Web client ID ending with '{GoogleAuthOptions.ClientIdSuffix}'.");
}

// Hosting platforms such as Render provide the port to listen on in the PORT environment variable.
// Bind to all interfaces on that port unless explicit URLs were configured (ASPNETCORE_URLS, launch profile).
// Without PORT, the defaults apply (launchSettings.json locally, ASPNETCORE_HTTP_PORTS=8080 in the Docker image).
var platformPort = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(platformPort) && string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    if (!int.TryParse(platformPort, out var port) || port is < 1 or > 65535)
        throw new InvalidOperationException($"PORT must be a number between 1 and 65535, got '{platformPort}'.");
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers();
builder.Services.AddFrontendCors(builder.Configuration, builder.Environment);
builder.Services.AddEmailSender(builder.Configuration);
builder.Services.Configure<AuthFeatureOptions>(builder.Configuration.GetSection(AuthFeatureOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.AddSingleton<IGoogleTokenValidator>(sp => new GoogleTokenValidator(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GoogleAuthOptions>>(), sp.GetRequiredService<ILogger<GoogleTokenValidator>>()));
builder.Services.AddScoped<GoogleSignInService>();
builder.Services.AddAuthRateLimiting(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            ClockSkew = TimeSpan.Zero,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    jwtKey
                )
            ),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        // Detailed reasons ("token expired at ...", "signature key was not found") only help attackers outside Development.
        options.IncludeErrorDetails = builder.Environment.IsDevelopment();

        options.Events = new JwtBearerEvents
        {
            // Controllers read the user id from this claim; reject tokens that do not carry a valid one
            // so that requests fail with 401 instead of 500.
            OnTokenValidated = context =>
            {
                if (!int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
                    context.Fail("The token does not contain a valid user id.");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddDbContext<GameNestDbContext>(options =>
    options.UseNpgsql(connectionString));

// Swagger
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()
    && app.Configuration.GetValue<bool>($"{AuthFeatureOptions.SectionName}:{nameof(AuthFeatureOptions.PasswordRegistrationEnabled)}")
    && !EmailServiceCollectionExtensions.IsResendConfigured(app.Configuration))
    app.Logger.LogWarning("No email provider is configured (Resend__ApiKey, Resend__FromEmail, Email__ConfirmationUrlBase): registration will return 503 until it is.");

// Swagger тільки в режимі розробки
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// HTTPS поки не чіпаємо
// app.UseHttpsRedirection();

// CORS runs before rate limiting and authentication: preflight (OPTIONS) requests are answered here
// without a token and without consuming rate-limit permits, and 401/429 responses still carry
// the CORS headers so the browser frontend can read them.
app.UseCors(FrontendCors.PolicyName);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        service = "GameNest Backend"
    });
});

app.MapControllers();

app.Run();

public partial class Program { }
