using GameNest_BackEnd.Data;
using GameNest_BackEnd.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;
namespace GameNest_BackEnd.Tests;
public class ApiWorkflowTests(ApiFactory factory):IClassFixture<ApiFactory>
{
 [Fact] public async Task Registration_confirmation_and_login_require_a_valid_one_time_token(){var c=factory.CreateClient();var response=await c.PostAsJsonAsync("/api/Auth/register",new{username="new-user",email="NEW@EXAMPLE.COM",password="safe-password"});Assert.Equal(HttpStatusCode.Created,response.StatusCode);var token=factory.SentConfirmations.Single(x=>x.Email=="new@example.com").Token;Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/Auth/login",new{email="new@example.com",password="safe-password"})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync("/api/Auth/confirm-email",new{email="new@example.com",token})).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/Auth/confirm-email",new{email="new@example.com",token})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync("/api/Auth/login",new{email="new@example.com",password="safe-password"})).StatusCode);}
 [Fact] public async Task User_cannot_read_or_modify_another_users_library_and_delete_keeps_catalog_game(){var(first,_,gameId)=await SeedUsersAndGame();var c=AuthenticatedClient(first.Id);Assert.Equal(HttpStatusCode.NotFound,(await c.PutAsJsonAsync($"/api/Library/{gameId}",new{status="Playing",purchased=true})).StatusCode);Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync($"/api/Library/{gameId}",new{status="Planned",purchased=false})).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync($"/api/Library/{gameId}",new{status="Planned",purchased=false})).StatusCode);Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/Library/{gameId}")).StatusCode);using var scope=factory.Services.CreateScope();Assert.NotNull(await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Games.FindAsync(gameId));}
 [Fact] public async Task Only_admin_can_approve_submission_and_approval_creates_catalog_game_once(){var(user,admin,_)=await SeedUsersAndGame();var c=AuthenticatedClient(user.Id);var create=await c.PostAsJsonAsync("/api/Submissions",new{type="NewGame",title="Submitted Game",description="",coverUrl="",trailerUrl="",developer="",publisher=""});var doc=JsonDocument.Parse(await create.Content.ReadAsStringAsync());var id=doc.RootElement.GetProperty("id").GetInt32();Assert.Equal(HttpStatusCode.Forbidden,(await c.PutAsync($"/api/Submissions/{id}/approve",null)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await AuthenticatedClient(admin.Id,"Admin").PutAsync($"/api/Submissions/{id}/approve",null)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await AuthenticatedClient(admin.Id,"Admin").PutAsync($"/api/Submissions/{id}/approve",null)).StatusCode);using var scope=factory.Services.CreateScope();Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<GameNestDbContext>().Games.CountAsync(g=>g.Title=="Submitted Game"));}
 private HttpClient AuthenticatedClient(int id,string role="User"){var c=factory.CreateClient();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme,CreateJwt(id,role));return c;}
 private static string CreateJwt(int id,string role)=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("GameNest.Tests","GameNest.Tests",[new Claim(ClaimTypes.NameIdentifier,id.ToString()),new Claim(ClaimTypes.Name,"test"),new Claim(ClaimTypes.Role,role)],expires:DateTime.UtcNow.AddHours(1),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-that-is-long-enough-for-hmac-sha256")),SecurityAlgorithms.HmacSha256)));
 private async Task<(User User,User Admin,int GameId)> SeedUsersAndGame(){using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<GameNestDbContext>();var s=Guid.NewGuid().ToString("N")[..8];var u=new User{Username="user"+s,Email="u"+s+"@test.local",PasswordHash="x",EmailConfirmed=true};var a=new User{Username="admin"+s,Email="a"+s+"@test.local",PasswordHash="x",EmailConfirmed=true,Role="Admin"};var t=new GameTitle{Title="title"+s};var g=new Game{Title=t.Title,GameTitle=t};db.AddRange(u,a,g);await db.SaveChangesAsync();return(u,a,g.Id);}
}
