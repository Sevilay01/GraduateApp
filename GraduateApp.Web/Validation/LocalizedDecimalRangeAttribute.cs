using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace GraduateApp.Web.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class LocalizedDecimalRangeAttribute
    : ValidationAttribute, IClientModelValidator
{
    public LocalizedDecimalRangeAttribute(int minimum, int maximum)
        : this(
            minimum.ToString(CultureInfo.InvariantCulture),
            maximum.ToString(CultureInfo.InvariantCulture))
    {
    }

    public LocalizedDecimalRangeAttribute(string minimum, string maximum)
    {
        Minimum = decimal.Parse(minimum, NumberStyles.Number, CultureInfo.InvariantCulture);
        Maximum = decimal.Parse(maximum, NumberStyles.Number, CultureInfo.InvariantCulture);

        if (Minimum > Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "Minimum değer maksimum değerden büyük olamaz.");
        }
    }

    public decimal Minimum { get; }
    public decimal Maximum { get; }
    public int MaximumFractionalDigits { get; set; } = -1;

    public override bool IsValid(object? value)
    {
        EnsureValidMaximumFractionalDigits();

        if (value is null)
        {
            return true;
        }

        if (value is not decimal decimalValue
            || decimalValue < Minimum
            || decimalValue > Maximum)
        {
            return false;
        }

        return MaximumFractionalDigits < 0
            || DecimalScale(decimalValue) <= MaximumFractionalDigits;
    }

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

        EnsureValidMaximumFractionalDigits();
        if (MaximumFractionalDigits >= 0)
        {
            MergeAttribute(
                context.Attributes,
                "data-val-localizeddecimal-scale",
                MaximumFractionalDigits.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void EnsureValidMaximumFractionalDigits()
    {
        if (MaximumFractionalDigits is < -1 or > 28)
        {
            throw new InvalidOperationException(
                "Ondalık basamak sınırı -1 ile 28 arasında olmalıdır.");
        }
    }

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;

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
