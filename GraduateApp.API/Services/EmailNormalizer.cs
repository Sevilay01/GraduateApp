using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.Services;

public interface IEmailNormalizer
{
    string Normalize(string email);
    bool TryNormalize(string? email, out string normalizedEmail);
}

public sealed class InvariantEmailNormalizer : IEmailNormalizer
{
    public string Normalize(string email) => email.Trim().ToUpperInvariant();

    public bool TryNormalize(string? email, out string normalizedEmail)
    {
        normalizedEmail = string.Empty;
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmedEmail = email.Trim();
        if (trimmedEmail.Any(char.IsControl)
            || trimmedEmail.Length > 254
            || !new EmailAddressAttribute().IsValid(trimmedEmail))
        {
            return false;
        }

        normalizedEmail = Normalize(trimmedEmail);
        return true;
    }
}
