namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class PhoneVerificationStateDto
    {
        public string UserId { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public string PhoneNumberMasked { get; set; } = string.Empty;
    }
}
