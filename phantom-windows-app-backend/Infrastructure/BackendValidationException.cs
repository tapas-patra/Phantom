namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class BackendValidationException : Exception
{
    public BackendValidationException(string message, string code = "validation_failed")
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
