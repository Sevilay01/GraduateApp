// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.dataset.disableOnSubmit !== "true") {
        return;
    }

    if (form.dataset.submitting === "true") {
        event.preventDefault();
        return;
    }

    form.dataset.submitting = "true";
    for (const submitControl of form.querySelectorAll('button[type="submit"], input[type="submit"]')) {
        submitControl.disabled = true;
        submitControl.setAttribute("aria-disabled", "true");
    }
});
