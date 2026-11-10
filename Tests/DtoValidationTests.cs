using GameNest_BackEnd.DTOs.Auth;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace GameNest_BackEnd.Tests;

public class DtoValidationTests
{
    [Fact]
    public void RegisterDto_rejects_an_invalid_email_and_short_password()
    {
        var dto = new RegisterDto { Username = "valid-name", Email = "not-an-email", Password = "short" };
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        Assert.False(valid);
        Assert.True(results.Count >= 2);
    }
}
