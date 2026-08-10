namespace Phantom.WindowsApp.Backend.Infrastructure;

public static class PasswordPolicy
{
    public const string Requirements = "Password must be at least 12 characters and include uppercase, lowercase, a number, and a special character, with no spaces.";

    public static void Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < 12
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character))
            || password.Any(char.IsWhiteSpace))
        {
            throw new BackendValidationException(Requirements);
        }
    }
}
