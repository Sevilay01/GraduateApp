namespace GraduateApp.API.Services;

public interface IEmailNormalizer
{
    string Normalize(string email);
}

public sealed class InvariantEmailNormalizer : IEmailNormalizer
{
    public string Normalize(string email) => email.Trim().ToUpperInvariant();
}
