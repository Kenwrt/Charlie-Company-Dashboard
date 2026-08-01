document.addEventListener("click", (event) => {
    const toggle = event.target.closest("[data-password-toggle]");
    if (!toggle) return;

    const input = document.getElementById(toggle.dataset.passwordToggle);
    if (!input) return;

    const showPassword = input.type === "password";
    input.type = showPassword ? "text" : "password";
    toggle.setAttribute("aria-pressed", showPassword.toString());
    toggle.setAttribute("aria-label", showPassword ? "Hide password" : "Show password");
    toggle.title = showPassword ? "Hide password" : "Show password";
});
