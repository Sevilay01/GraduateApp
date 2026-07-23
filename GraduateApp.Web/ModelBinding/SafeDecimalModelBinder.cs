using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GraduateApp.Web.ModelBinding;

public sealed class SafeDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var modelType = Nullable.GetUnderlyingType(context.Metadata.ModelType)
            ?? context.Metadata.ModelType;
        return modelType == typeof(decimal) ? new SafeDecimalModelBinder() : null;
    }
}

internal sealed class SafeDecimalModelBinder : IModelBinder
{
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);
        var attemptedValue = valueProviderResult.FirstValue?.Trim();
        var isNullable = Nullable.GetUnderlyingType(bindingContext.ModelType) == typeof(decimal);
        if (string.IsNullOrEmpty(attemptedValue))
        {
            if (isNullable)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            else
            {
                AddInvalidValueError(bindingContext, attemptedValue ?? string.Empty);
            }

            return Task.CompletedTask;
        }

        if (TryParse(attemptedValue, valueProviderResult.Culture, out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            AddInvalidValueError(bindingContext, attemptedValue);
        }

        return Task.CompletedTask;
    }

    private static bool TryParse(string value, CultureInfo valueCulture, out decimal result)
    {
        if (decimal.TryParse(
            value,
            DecimalStyles,
            CultureInfo.InvariantCulture,
            out result))
        {
            return true;
        }

        if (!Equals(valueCulture, CultureInfo.InvariantCulture)
            && decimal.TryParse(value, DecimalStyles, valueCulture, out result))
        {
            return true;
        }

        var currentCulture = CultureInfo.CurrentCulture;
        return !Equals(currentCulture, CultureInfo.InvariantCulture)
            && !Equals(currentCulture, valueCulture)
            && decimal.TryParse(value, DecimalStyles, currentCulture, out result);
    }

    private static void AddInvalidValueError(
        ModelBindingContext bindingContext,
        string attemptedValue) =>
        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            bindingContext.ModelMetadata.ModelBindingMessageProvider
                .AttemptedValueIsInvalidAccessor(attemptedValue, bindingContext.ModelName));
}
