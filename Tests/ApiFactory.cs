using GameNest_BackEnd.Data;
using GameNest_BackEnd.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace GameNest_BackEnd.Tests;
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
 private readonly SqliteConnection _connection=new("Data Source=:memory:"); public List<(string Email,string Token)> SentConfirmations {get;}=[];
 public ApiFactory(){Environment.SetEnvironmentVariable("Jwt__Key","test-signing-key-that-is-long-enough-for-hmac-sha256");Environment.SetEnvironmentVariable("Jwt__Issuer","GameNest.Tests");Environment.SetEnvironmentVariable("Jwt__Audience","GameNest.Tests");Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection","unused");}
 protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration(c=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key","test-signing-key-that-is-long-enough-for-hmac-sha256"},{"Jwt:Issuer","GameNest.Tests"},{"Jwt:Audience","GameNest.Tests"},{"ConnectionStrings:DefaultConnection","unused"}}));builder.ConfigureServices(services=>{foreach(var d in services.Where(d=>d.ServiceType.Assembly.GetName().Name?.StartsWith("Npgsql",StringComparison.Ordinal)==true||d.ImplementationType?.Assembly.GetName().Name?.StartsWith("Npgsql",StringComparison.Ordinal)==true||d.ServiceType.GenericTypeArguments.Contains(typeof(GameNestDbContext))||d.ServiceType.GenericTypeArguments.Contains(typeof(DbContextOptions<GameNestDbContext>))).ToList())services.Remove(d);services.RemoveAll<DbContextOptions<GameNestDbContext>>();services.RemoveAll<GameNestDbContext>();services.AddDbContext<GameNestDbContext>(o=>o.UseSqlite(_connection));services.RemoveAll<IEmailSender>();services.AddSingleton<IEmailSender>(new TestEmailSender(SentConfirmations));});}
 public async Task InitializeAsync(){await _connection.OpenAsync();using var scope=Services.CreateScope();await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Database.EnsureCreatedAsync();}
 Task IAsyncLifetime.DisposeAsync(){_connection.Dispose();Dispose();return Task.CompletedTask;}
 private sealed class TestEmailSender(List<(string Email,string Token)> messages):IEmailSender { public Task SendEmailConfirmationAsync(string email,string token,CancellationToken cancellationToken=default){messages.Add((email,token));return Task.CompletedTask;} }
}
