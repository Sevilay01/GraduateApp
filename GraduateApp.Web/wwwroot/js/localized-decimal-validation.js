(function ($) {
    "use strict";

    if (!$?.validator?.unobtrusive) {
        return;
    }

    const decimalPattern = /^[+-]?(?:\d+(?:[.,]\d+)?|[.,]\d+)$/;

    function parseDecimal(value) {
        const normalized = value.trim();
        if (!decimalPattern.test(normalized)) {
            return null;
        }

        const parsed = Number(normalized.replace(",", "."));
        return Number.isFinite(parsed) ? parsed : null;
    }

    function fractionalDigitCount(value) {
        const normalized = value.trim();
        const separatorIndex = Math.max(
            normalized.lastIndexOf("."),
            normalized.lastIndexOf(","));

        return separatorIndex < 0
            ? 0
            : normalized.length - separatorIndex - 1;
    }

    $.validator.addMethod("localizeddecimal", function (value, element, parameters) {
        if (this.optional(element)) {
            return true;
        }

        const parsed = parseDecimal(value);
        const hasScale = parameters.scale !== undefined
            && parameters.scale !== null
            && parameters.scale !== "";
        const maximumFractionalDigits = hasScale
            ? Number(parameters.scale)
            : null;

        return parsed !== null
            && parsed >= Number(parameters.min)
            && parsed <= Number(parameters.max)
            && (maximumFractionalDigits === null
                || fractionalDigitCount(value) <= maximumFractionalDigits);
    });

    $.validator.unobtrusive.adapters.add(
        "localizeddecimal",
        ["min", "max", "scale"],
        function (options) {
            options.rules.number = false;
            options.rules.localizeddecimal = {
                min: options.params.min,
                max: options.params.max,
                scale: options.params.scale
            };
            options.messages.localizeddecimal = options.message;
        });
}(jQuery));
