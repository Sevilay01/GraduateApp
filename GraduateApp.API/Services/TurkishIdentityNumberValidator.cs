namespace GraduateApp.API.Services;

public static class TurkishIdentityNumberValidator
{
    public static bool IsValid(string value)
    {
        if (value.Length != 11 || value[0] == '0' || value.Any(character => !char.IsDigit(character)))
        {
            return false;
        }

        var digits = value.Select(character => character - '0').ToArray();
        var oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var evenSum = digits[1] + digits[3] + digits[5] + digits[7];

        return ((oddSum * 7) - evenSum) % 10 == digits[9]
            && digits.Take(10).Sum() % 10 == digits[10];
    }
}
