namespace GraduateApp.API.Services;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    public static bool IsValid(string password) =>
        password.Length >= MinimumLength
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        && password.Any(character => !char.IsLetterOrDigit(character));

    public const string ValidationMessage =
        "Parola en az 12 karakter olmalı; büyük harf, küçük harf, rakam ve özel karakter içermelidir.";
}
