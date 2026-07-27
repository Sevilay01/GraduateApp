const focusRequestedPageContext = () => {
    let requestedTarget = null;
    if (window.location.hash.length > 1) {
        try {
            requestedTarget = document.getElementById(decodeURIComponent(window.location.hash.substring(1)));
        } catch {
            requestedTarget = null;
        }
    }

    requestedTarget ??= document.querySelector('[data-auto-focus="true"]');
    if (!(requestedTarget instanceof HTMLElement)) {
        return;
    }

    const focusTarget = requestedTarget.matches("[data-fragment-focus]")
        ? requestedTarget
        : requestedTarget.querySelector("[data-fragment-focus]");
    if (!(focusTarget instanceof HTMLElement)) {
        return;
    }

    window.requestAnimationFrame(() => {
        focusTarget.focus({ preventScroll: true });
        requestedTarget.scrollIntoView({ block: "start" });
    });
};

focusRequestedPageContext();
window.addEventListener("hashchange", focusRequestedPageContext);

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

for (const input of document.querySelectorAll("input[data-password-reveal]")) {
    if (!(input instanceof HTMLInputElement) || !input.id || input.parentElement?.querySelector("[data-password-toggle]")) {
        continue;
    }

    const button = document.createElement("button");
    button.type = "button";
    button.className = "password-toggle";
    button.dataset.passwordToggle = "";
    button.setAttribute("aria-controls", input.id);
    button.setAttribute("aria-pressed", "false");
    button.setAttribute("aria-label", "Parolayı göster");
    button.textContent = "Göster";
    button.addEventListener("click", () => {
        const isVisible = input.type === "text";
        input.type = isVisible ? "password" : "text";
        button.setAttribute("aria-pressed", String(!isVisible));
        button.setAttribute("aria-label", isVisible ? "Parolayı göster" : "Parolayı gizle");
        button.textContent = isVisible ? "Göster" : "Gizle";
    });
    input.parentElement?.append(button);
}
