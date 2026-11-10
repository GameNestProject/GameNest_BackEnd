using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace GameNest_BackEnd.Services;

public class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

/// <summary>
/// In-memory, per-instance rate limiting for the authentication endpoints, partitioned by client IP
/// (and endpoint path). Limits are read from configuration when a partition is first created:
/// <c>RateLimiting:Auth</c> and <c>RateLimiting:ResendConfirmation</c>.
/// The client IP is the direct connection address; forwarded headers are not trusted unless
/// the forwarded-headers middleware is configured with known proxies.
/// </summary>
public static class AuthRateLimiting
{
    public const string AuthPolicy = "auth";
    public const string ResendConfirmationPolicy = "resend-confirmation";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitPolicyOptions>(AuthPolicy)
            .Configure(o => { o.PermitLimit = 10; o.WindowSeconds = 60; })
            .Bind(configuration.GetSection("RateLimiting:Auth"));
        services.AddOptions<RateLimitPolicyOptions>(ResendConfirmationPolicy)
            .Configure(o => { o.PermitLimit = 5; o.WindowSeconds = 900; })
            .Bind(configuration.GetSection("RateLimiting:ResendConfirmation"));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await context.HttpContext.Response.WriteAsJsonAsync(new { code = "too_many_requests" }, cancellationToken);
            };
            options.AddPolicy(AuthPolicy, httpContext => CreatePartition(httpContext, AuthPolicy));
            options.AddPolicy(ResendConfirmationPolicy, httpContext => CreatePartition(httpContext, ResendConfirmationPolicy));
        });
        return services;
    }

    private static RateLimitPartition<string> CreatePartition(HttpContext httpContext, string policy)
    {
        var settings = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitPolicyOptions>>().Get(policy);
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{clientIp}|{httpContext.Request.Path.Value?.ToLowerInvariant()}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, settings.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, settings.WindowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
