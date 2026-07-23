using System.Globalization;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.ModelBinding;
using GraduateApp.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GraduateApp.Tests;

public sealed class SafeDecimalModelBindingTests
{
    [Fact]
    public Task Default_binder_reproduces_scaled_canonical_decimal_failure_under_turkish_culture() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices(useSafeDecimalBinder: false);
            var values = ValidCriterionValues("100.0000");
            values["SourceType"] = EvaluationCriterionSourceType.ExamScore.ToString();
            values["ExamId"] = "6";
            values["WeightBasisPoints"] = "5000";

            var (model, actionContext) =
                await BindAndValidateAsync<EvaluationCriterionFormViewModel>(services, values);

            var maximumRawScoreState = AssertModelState(
                actionContext,
                nameof(EvaluationCriterionFormViewModel.MaximumRawScore),
                "100.0000");
            Assert.NotEmpty(maximumRawScoreState.Errors);
            Assert.Equal(1000000m, model.MaximumRawScore);
            Assert.Equal(5000, model.WeightBasisPoints);
            var weightState =
                actionContext.ModelState[nameof(EvaluationCriterionFormViewModel.WeightBasisPoints)];
            Assert.NotNull(weightState);
            Assert.Empty(weightState.Errors);
        });

    [Fact]
    public Task Default_binder_can_silently_misread_a_canonical_fraction_under_turkish_culture() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices(useSafeDecimalBinder: false);

            var (model, actionContext) =
                await BindAndValidateAsync<EvaluationCriterionFormViewModel>(
                    services,
                    ValidCriterionValues("87.5"));

            Assert.True(actionContext.ModelState.IsValid);
            Assert.Equal(875m, model.MaximumRawScore);
            AssertModelState(
                actionContext,
                nameof(EvaluationCriterionFormViewModel.MaximumRawScore),
                "87.5");
        });

    [Theory]
    [InlineData("100", "100")]
    [InlineData("100.0000", "100")]
    [InlineData("87.5", "87.5")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("99999", "99999")]
    public Task Canonical_decimal_values_bind_and_validate_under_turkish_culture(
        string attemptedValue,
        string expectedValue) =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();

            var (model, actionContext) =
                await BindAndValidateAsync<EvaluationCriterionFormViewModel>(
                    services,
                    ValidCriterionValues(attemptedValue));

            Assert.True(actionContext.ModelState.IsValid);
            Assert.Equal(
                decimal.Parse(expectedValue, CultureInfo.InvariantCulture),
                model.MaximumRawScore);
            AssertModelState(
                actionContext,
                nameof(EvaluationCriterionFormViewModel.MaximumRawScore),
                attemptedValue);
        });

    [Theory]
    [InlineData("87,5", "87.5")]
    [InlineData("0,0001", "0.0001")]
    [InlineData("100", "100")]
    public Task Turkish_decimal_values_bind_and_validate(
        string attemptedValue,
        string expectedValue) =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();

            var (model, actionContext) =
                await BindAndValidateAsync<EvaluationCriterionFormViewModel>(
                    services,
                    ValidCriterionValues(attemptedValue));

            Assert.True(actionContext.ModelState.IsValid);
            Assert.Equal(
                decimal.Parse(expectedValue, CultureInfo.InvariantCulture),
                model.MaximumRawScore);
        });

    [Theory]
    [InlineData("0")]
    [InlineData("99999.0001")]
    public Task Criterion_range_boundaries_reject_out_of_range_values(string attemptedValue) =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();

            var (_, actionContext) =
                await BindAndValidateAsync<EvaluationCriterionFormViewModel>(
                    services,
                    ValidCriterionValues(attemptedValue));

            var state = AssertModelState(
                actionContext,
                nameof(EvaluationCriterionFormViewModel.MaximumRawScore),
                attemptedValue);
            Assert.NotEmpty(state.Errors);
            Assert.False(actionContext.ModelState.IsValid);
        });

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("1.2.3")]
    [InlineData("1,2,3")]
    [InlineData("1,234.56")]
    [InlineData("1.234,56")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public Task Invalid_or_grouped_nonnullable_decimal_values_are_rejected(string attemptedValue) =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();

            var (_, actionContext) =
                await BindAndValidateAsync<EvaluationCriterionFormViewModel>(
                    services,
                    ValidCriterionValues(attemptedValue));

            var state = AssertModelState(
                actionContext,
                nameof(EvaluationCriterionFormViewModel.MaximumRawScore),
                attemptedValue);
            Assert.NotEmpty(state.Errors);
            Assert.False(actionContext.ModelState.IsValid);
        });

    [Fact]
    public Task Empty_nullable_decimal_binds_null_without_a_conversion_error() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();

            var (result, actionContext) =
                await BindValueAsync(services, typeof(decimal?), "rawScore", string.Empty);

            Assert.True(result.IsModelSet);
            Assert.Null(result.Model);
            Assert.Empty(AssertModelState(actionContext, "rawScore", string.Empty).Errors);
        });

    [Theory]
    [InlineData("87.5", "87.5")]
    [InlineData("87,5", "87.5")]
    public Task Manual_score_parameter_accepts_canonical_and_turkish_decimals(
        string attemptedValue,
        string expectedValue) =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();

            var (result, actionContext) =
                await BindValueAsync(services, typeof(decimal?), "rawScore", attemptedValue);

            Assert.Empty(AssertModelState(actionContext, "rawScore", attemptedValue).Errors);
            Assert.Equal(
                decimal.Parse(expectedValue, CultureInfo.InvariantCulture),
                Assert.IsType<decimal>(result.Model));
        });

    [Fact]
    public Task Student_exam_score_accepts_canonical_decimal_with_form_prefix() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();
            var values = new Dictionary<string, StringValues>(StringComparer.Ordinal)
            {
                ["Form.ExamId"] = "1",
                ["Form.Score"] = "87.5",
                ["Form.ExamDate"] = "2026-07-23"
            };

            var (model, actionContext) =
                await BindAndValidateAsync<StudentExamScoreInputViewModel>(
                    services,
                    values,
                    modelName: "Form");

            Assert.True(actionContext.ModelState.IsValid);
            Assert.Equal(87.5m, model.Score);
            AssertModelState(actionContext, "Form.Score", "87.5");
        });

    [Fact]
    public Task Profile_gno_and_offering_minimum_score_accept_canonical_decimals() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            await using var services = CreateServices();
            var profileValues = new Dictionary<string, StringValues>(StringComparer.Ordinal)
            {
                ["FirstName"] = "Ada",
                ["LastName"] = "Lovelace",
                ["Email"] = "ada@example.test",
                ["Gno"] = "3.25"
            };
            var requirementValues = new Dictionary<string, StringValues>(StringComparer.Ordinal)
            {
                ["ExamId"] = "1",
                ["MinimumScore"] = "87.5"
            };

            var (profile, profileContext) =
                await BindAndValidateAsync<StudentProfileViewModel>(services, profileValues);
            var (requirement, requirementContext) =
                await BindAndValidateAsync<ProgramOfferingRequirementInputViewModel>(
                    services,
                    requirementValues);

            Assert.True(profileContext.ModelState.IsValid);
            Assert.Equal(3.25m, profile.Gno);
            AssertModelState(profileContext, nameof(StudentProfileViewModel.Gno), "3.25");
            Assert.True(requirementContext.ModelState.IsValid);
            Assert.Equal(87.5m, requirement.MinimumScore);
            AssertModelState(
                requirementContext,
                nameof(ProgramOfferingRequirementInputViewModel.MinimumScore),
                "87.5");
        });

    private static ServiceProvider CreateServices(bool useSafeDecimalBinder = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews(options =>
            {
                if (useSafeDecimalBinder)
                {
                    options.ModelBinderProviders.Insert(0, new SafeDecimalModelBinderProvider());
                }
            })
            .AddApplicationPart(typeof(AdminController).Assembly);
        return services.BuildServiceProvider();
    }

    private static async Task<(T Model, ActionContext ActionContext)> BindAndValidateAsync<T>(
        IServiceProvider services,
        Dictionary<string, StringValues> values,
        string modelName = "")
        where T : class
    {
        var (bindingResult, actionContext) =
            await BindAsync(services, typeof(T), values, modelName);
        var model = Assert.IsType<T>(bindingResult.Model);
        services.GetRequiredService<IObjectModelValidator>()
            .Validate(actionContext, validationState: null, prefix: modelName, model);
        return (model, actionContext);
    }

    private static Task<(ModelBindingResult Result, ActionContext ActionContext)> BindValueAsync(
        IServiceProvider services,
        Type modelType,
        string modelName,
        string attemptedValue) =>
        BindAsync(
            services,
            modelType,
            new Dictionary<string, StringValues>(StringComparer.Ordinal)
            {
                [modelName] = attemptedValue
            },
            modelName);

    private static async Task<(ModelBindingResult Result, ActionContext ActionContext)> BindAsync(
        IServiceProvider services,
        Type modelType,
        Dictionary<string, StringValues> values,
        string modelName)
    {
        var form = new FormCollection(values);
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Features.Set<IFormFeature>(new FormFeature(form));
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor(),
            new ModelStateDictionary());
        var metadata = services.GetRequiredService<IModelMetadataProvider>()
            .GetMetadataForType(modelType);
        var binder = services.GetRequiredService<IModelBinderFactory>()
            .CreateBinder(
                new ModelBinderFactoryContext
                {
                    Metadata = metadata,
                    BindingInfo = new BindingInfo()
                });
        var valueProvider = new FormValueProvider(
            BindingSource.Form,
            form,
            CultureInfo.GetCultureInfo("tr-TR"));
        var bindingContext = DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadata,
            bindingInfo: null,
            modelName);

        await binder.BindModelAsync(bindingContext);
        return (bindingContext.Result, actionContext);
    }

    private static ModelStateEntry AssertModelState(
        ActionContext actionContext,
        string fieldName,
        string attemptedValue)
    {
        var state = actionContext.ModelState[fieldName];
        Assert.NotNull(state);
        Assert.Equal(attemptedValue, state.AttemptedValue);
        return state;
    }

    private static Dictionary<string, StringValues> ValidCriterionValues(string maximumRawScore) =>
        new(StringComparer.Ordinal)
        {
            ["ProgramOfferingId"] = "42",
            ["Code"] = "GPA",
            ["DisplayName"] = "Lisans GNO",
            ["SourceType"] = EvaluationCriterionSourceType.UndergraduateGpa.ToString(),
            ["ExamId"] = string.Empty,
            ["WeightBasisPoints"] = "10000",
            ["MaximumRawScore"] = maximumRawScore,
            ["TieBreakPriority"] = "1"
        };

    private static async Task ExecuteInTurkishCultureAsync(Func<Task> action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            await action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
