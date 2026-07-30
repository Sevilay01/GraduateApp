using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using GraduateApp.API.DTOs;
using GraduateApp.Web.Models;
using GraduateApp.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace GraduateApp.Tests;

public sealed class EvaluationCriterionDecimalValidationTests
{
    [Fact]
    public void Web_maximum_raw_score_client_validation_metadata_is_generated_under_turkish_culture()
    {
        ExecuteInCulture("tr-TR", () =>
        {
            var property = typeof(EvaluationCriterionFormViewModel)
                .GetProperty(nameof(EvaluationCriterionFormViewModel.MaximumRawScore))!;
            var localizedDecimal =
                property.GetCustomAttribute<LocalizedDecimalRangeAttribute>()!;
            var metadataProvider = new EmptyModelMetadataProvider();
            var metadata = metadataProvider.GetMetadataForProperty(
                typeof(EvaluationCriterionFormViewModel),
                nameof(EvaluationCriterionFormViewModel.MaximumRawScore));
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

            localizedDecimal.AddValidation(new ClientModelValidationContext(
                new ActionContext(),
                metadata,
                metadataProvider,
                attributes));

            Assert.Equal("true", attributes["data-val"]);
            Assert.Equal(
                "Maksimum ham puan 0,0001 ile 99999 arasında geçerli bir ondalık sayı olmalıdır.",
                attributes["data-val-localizeddecimal"]);
            Assert.Equal("0.0001", attributes["data-val-localizeddecimal-min"]);
            Assert.Equal("99999", attributes["data-val-localizeddecimal-max"]);
            Assert.DoesNotContain("data-val-range", attributes.Keys);
        });
    }

    [Theory]
    [InlineData("0.0001", true)]
    [InlineData("99999", true)]
    [InlineData("0", false)]
    [InlineData("99999.0001", false)]
    public void Web_maximum_raw_score_enforces_inclusive_bounds_under_turkish_culture(
        string invariantValue,
        bool expectedIsValid)
    {
        ExecuteInCulture("tr-TR", () =>
        {
            var model = new EvaluationCriterionFormViewModel
            {
                Code = "GNO",
                DisplayName = "Lisans not ortalaması",
                WeightBasisPoints = 1,
                MaximumRawScore = decimal.Parse(invariantValue, CultureInfo.InvariantCulture),
                TieBreakPriority = 1
            };

            var results = Validate(model);

            if (expectedIsValid)
            {
                Assert.Empty(results);
            }
            else
            {
                var error = Assert.Single(results);
                Assert.Equal(
                    "Maksimum ham puan 0,0001 ile 99999 arasında geçerli bir ondalık sayı olmalıdır.",
                    error.ErrorMessage);
                Assert.Contains(
                    nameof(EvaluationCriterionFormViewModel.MaximumRawScore),
                    error.MemberNames);
            }
        });
    }

    [Fact]
    public void String_decimal_range_limits_are_parsed_in_invariant_culture()
    {
        var modelTypes = typeof(EvaluationCriterionFormViewModel).Assembly.ExportedTypes
            .Concat(typeof(EvaluationCriterionCreateDto).Assembly.ExportedTypes);

        var decimalRanges = modelTypes
            .SelectMany(type => type.GetProperties())
            .SelectMany(property => property
                .GetCustomAttributes<RangeAttribute>()
                .Select(attribute => (Property: property, Attribute: attribute)))
            .Where(item => item.Attribute.OperandType == typeof(decimal))
            .ToArray();

        Assert.NotEmpty(decimalRanges);
        Assert.All(
            decimalRanges,
            item => Assert.True(
                item.Attribute.ParseLimitsInInvariantCulture,
                $"{item.Property.DeclaringType!.FullName}.{item.Property.Name}"));
    }

    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static void ExecuteInCulture(string cultureName, Action action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
