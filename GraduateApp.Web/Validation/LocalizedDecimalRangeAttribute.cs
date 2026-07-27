using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace GraduateApp.Web.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LocalizedDecimalRangeAttribute(int minimum, int maximum)
    : ValidationAttribute, IClientModelValidator
{
    public decimal Minimum { get; } = minimum;
    public decimal Maximum { get; } = maximum;

    public override bool IsValid(object? value) =>
        value is null
        || value is decimal decimalValue
            && decimalValue >= Minimum
            && decimalValue <= Maximum;

    public void AddValidation(ClientModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MergeAttribute(context.Attributes, "data-val", "true");
        MergeAttribute(
            context.Attributes,
            "data-val-localizeddecimal",
            FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        MergeAttribute(
            context.Attributes,
            "data-val-localizeddecimal-min",
            Minimum.ToString(CultureInfo.InvariantCulture));
        MergeAttribute(
            context.Attributes,
            "data-val-localizeddecimal-max",
            Maximum.ToString(CultureInfo.InvariantCulture));
    }

    private static void MergeAttribute(
        IDictionary<string, string> attributes,
        string key,
        string value)
    {
        if (!attributes.ContainsKey(key))
        {
            attributes.Add(key, value);
        }
    }
}
