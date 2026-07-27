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

    $.validator.addMethod("localizeddecimal", function (value, element, parameters) {
        if (this.optional(element)) {
            return true;
        }

        const parsed = parseDecimal(value);
        return parsed !== null
            && parsed >= Number(parameters.min)
            && parsed <= Number(parameters.max);
    });

    $.validator.unobtrusive.adapters.add(
        "localizeddecimal",
        ["min", "max"],
        function (options) {
            options.rules.number = false;
            options.rules.localizeddecimal = {
                min: options.params.min,
                max: options.params.max
            };
            options.messages.localizeddecimal = options.message;
        });
}(jQuery));
