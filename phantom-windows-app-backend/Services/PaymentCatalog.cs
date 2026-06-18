using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class PaymentCatalog
{
    private const decimal PremiumDebtSettlementRateInrPerCredit = 599m;

    private static readonly PaymentPackDto[] ProPacks =
    [
        new()
        {
            Target = AccessModeResolver.ProByo,
            PackCode = "pro_3",
            Label = "Pro Starter",
            Credits = 3m,
            DisplayAmountInr = 699m,
            AmountMinor = 69900,
            Description = "BYO provider mode with 3 Pro app-usage credits."
        },
        new()
        {
            Target = AccessModeResolver.ProByo,
            PackCode = "pro_8",
            Label = "Pro Builder",
            Credits = 8m,
            DisplayAmountInr = 1499m,
            AmountMinor = 149900,
            Description = "BYO provider mode with 8 Pro app-usage credits."
        },
        new()
        {
            Target = AccessModeResolver.ProByo,
            PackCode = "pro_15",
            Label = "Pro Deep Run",
            Credits = 15m,
            DisplayAmountInr = 2499m,
            AmountMinor = 249900,
            Description = "BYO provider mode with 15 Pro app-usage credits."
        }
    ];

    private static readonly PaymentPackDto[] PremiumPacks =
    [
        new()
        {
            Target = AccessModeResolver.Premium,
            PackCode = "premium_3",
            Label = "Premium Starter",
            Credits = 3m,
            DisplayAmountInr = 1799m,
            AmountMinor = 179900,
            Description = "Managed AI access with 3 Premium credits."
        },
        new()
        {
            Target = AccessModeResolver.Premium,
            PackCode = "premium_8",
            Label = "Premium Builder",
            Credits = 8m,
            DisplayAmountInr = 3799m,
            AmountMinor = 379900,
            Description = "Managed AI access with 8 Premium credits."
        },
        new()
        {
            Target = AccessModeResolver.Premium,
            PackCode = "premium_15",
            Label = "Premium Deep Run",
            Credits = 15m,
            DisplayAmountInr = 5599m,
            AmountMinor = 559900,
            Description = "Managed AI access with 15 Premium credits."
        }
    ];

    public PaymentCatalogResponseDto BuildCatalog(DesktopAccountRecord account, string razorpayKeyId)
    {
        return new PaymentCatalogResponseDto
        {
            RazorpayKeyId = razorpayKeyId,
            ProPacks = ProPacks.Select(Clone).ToArray(),
            PremiumPacks = PremiumPacks.Select(Clone).ToArray(),
            PremiumDebtSettlement = account.PremiumNegativeCredits > 0m
                ? new
                {
                    target = "premium_debt_settlement",
                    label = "Settle Premium Debt",
                    outstandingCredits = account.PremiumNegativeCredits,
                    displayAmountInr = GetDebtSettlementAmountInr(account.PremiumNegativeCredits),
                    amountMinor = GetDebtSettlementAmountMinor(account.PremiumNegativeCredits)
                }
                : null
        };
    }

    public PaymentPackDto ResolvePack(string target, string packCode)
    {
        var source = string.Equals(target, AccessModeResolver.ProByo, StringComparison.OrdinalIgnoreCase)
            ? ProPacks
            : string.Equals(target, AccessModeResolver.Premium, StringComparison.OrdinalIgnoreCase)
                ? PremiumPacks
                : [];

        var pack = source.FirstOrDefault(item => string.Equals(item.PackCode, packCode, StringComparison.OrdinalIgnoreCase));
        if (pack == null)
        {
            throw new InvalidOperationException("Unknown payment pack.");
        }

        return Clone(pack);
    }

    public decimal GetDebtSettlementAmountInr(decimal premiumDebtCredits)
    {
        return Math.Round(premiumDebtCredits * PremiumDebtSettlementRateInrPerCredit, 0, MidpointRounding.AwayFromZero);
    }

    public int GetDebtSettlementAmountMinor(decimal premiumDebtCredits)
    {
        return decimal.ToInt32(GetDebtSettlementAmountInr(premiumDebtCredits) * 100m);
    }

    private static PaymentPackDto Clone(PaymentPackDto input)
    {
        return new PaymentPackDto
        {
            Target = input.Target,
            PackCode = input.PackCode,
            Label = input.Label,
            Credits = input.Credits,
            DisplayAmountInr = input.DisplayAmountInr,
            AmountMinor = input.AmountMinor,
            Description = input.Description
        };
    }
}
