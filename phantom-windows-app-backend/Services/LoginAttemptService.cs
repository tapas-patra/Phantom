using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class LoginAttemptService
{
    private readonly BackendOptions _options;
    private readonly LoginAttemptRepository _repository;

    public LoginAttemptService(BackendOptions options, LoginAttemptRepository repository)
    {
        _options = options;
        _repository = repository;
    }

    public void EnsureNotBlocked(string email, string ipAddress)
    {
        var failures = _repository.CountRecentFailures(
            email,
            ipAddress,
            DateTime.UtcNow.AddMinutes(-_options.LoginAttemptWindowMinutes));

        if (failures >= _options.MaxFailedLoginAttempts)
        {
            throw new BackendValidationException(
                "Too many failed login attempts. Please wait and try again.");
        }
    }

    public void Record(string email, string ipAddress, bool succeeded)
    {
        _repository.Record(email, ipAddress, succeeded);
    }
}
