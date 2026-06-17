namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class BackendValidationException : Exception
{
    public BackendValidationException(string message)
        : base(message)
    {
    }
}
