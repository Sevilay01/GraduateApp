using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GraduateApp.API.DTOs;
using GraduateApp.Web.Models;

namespace GraduateApp.Tests;

public sealed class ProgramOfferingDecimalValidationTests
{
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void Web_minimum_score_range_is_culture_independent(string cultureName)
    {
        var validModel = new ProgramOfferingRequirementInputViewModel
        {
            ExamId = 1,
            MinimumScore = 55.50m
        };
        var invalidModel = new ProgramOfferingRequirementInputViewModel
        {
            ExamId = 1,
            MinimumScore = 1000m
        };

        AssertRangeValidation(cultureName, validModel, invalidModel);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void Api_minimum_score_range_is_culture_independent(string cultureName)
    {
        var validDto = new ProgramOfferingRequirementInputDto
        {
            ExamId = 1,
            MinimumScore = 55.50m
        };
        var invalidDto = new ProgramOfferingRequirementInputDto
        {
            ExamId = 1,
            MinimumScore = 1000m
        };

        AssertRangeValidation(cultureName, validDto, invalidDto);
    }

    private static void AssertRangeValidation(string cultureName, object validModel, object invalidModel)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            Assert.Empty(Validate(validModel));

            var error = Assert.Single(Validate(invalidModel));
            Assert.Equal("Puan 0 ile 999,99 arasında olmalıdır.", error.ErrorMessage);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
