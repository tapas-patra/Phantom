using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Services;

public static class AccessModeResolver
{
    private const decimal LegacyTrialPremiumCreditGrant = 0.5m;
    public const string Free = "free";
    public const string ProByo = "pro_byo";
    public const string Premium = "premium";

    public static string GetEffectiveAccessTier(DesktopAccountRecord account)
    {
        if (string.Equals(account.AccessTier, Free, StringComparison.OrdinalIgnoreCase)
            && account.ProAvailableCredits <= 0m
            && account.PremiumNegativeCredits <= 0m
            && account.PremiumAvailableCredits <= LegacyTrialPremiumCreditGrant)
        {
            return Free;
        }

        if (account.PremiumAvailableCredits > 0m)
        {
            return Premium;
        }

        if (account.ProAvailableCredits > 0m)
        {
            return ProByo;
        }

        return Free;
    }

    public static string GetPlanLabel(DesktopAccountRecord account)
    {
        return GetEffectiveAccessTier(account) switch
        {
            Premium => "Premium",
            ProByo => "Pro BYO",
            _ => "Free"
        };
    }

    public static bool HasPremiumFeatureAccess(DesktopAccountRecord account)
    {
        return string.Equals(GetEffectiveAccessTier(account), Premium, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFree(DesktopAccountRecord account)
    {
        return string.Equals(GetEffectiveAccessTier(account), Free, StringComparison.OrdinalIgnoreCase);
    }
}
