import Foundation

enum AccountAccess {
    static func isFree(_ tier: String?) -> Bool { tier?.lowercased() == "free" }

    static func hasPremium(tier: String?, credits: Decimal) -> Bool {
        !isFree(tier) && (credits > 0 || tier?.lowercased() == "premium")
    }

    static func hasBYO(tier: String?, credits: Decimal) -> Bool {
        !isFree(tier) && (credits > 0 || tier?.lowercased() == "pro_byo")
    }

    static func usesBYO(
        tier: String?,
        proCredits: Decimal,
        premiumCredits: Decimal,
        preferBYO: Bool
    ) -> Bool {
        hasBYO(tier: tier, credits: proCredits)
            && (!hasPremium(tier: tier, credits: premiumCredits) || preferBYO || premiumCredits <= 0)
    }
}
