using GameNest_BackEnd.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi;
using GameNest_BackEnd.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddControllers();
builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();

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
            )
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

// Swagger тільки в режимі розробки
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// HTTPS поки не чіпаємо
// app.UseHttpsRedirection();

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
