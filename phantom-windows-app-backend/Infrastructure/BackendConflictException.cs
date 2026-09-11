namespace Phantom.WindowsApp.Backend.Infrastructure;

/// <summary>
/// Raised for state conflicts that the global exception handler maps to HTTP 409
/// (instead of the 400 used for <see cref="BackendValidationException"/>). Used for
/// conflicts such as "this desktop already has an active companion."
/// </summary>
public sealed class BackendConflictException : Exception
{
    public BackendConflictException(string message, string code = "conflict")
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
