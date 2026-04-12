namespace ReadyToMigrate.Services;

public class CouponService
{
    private readonly ILogger<CouponService> _logger;

    public CouponService(ILogger<CouponService> logger)
    {
        _logger = logger;
    }

    public Coupon CreateCoupon(string code, int validDays)
    {
        return new Coupon
        {
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(validDays)
        };
    }

    public bool IsValid(Coupon coupon)
    {
        if (DateTime.UtcNow > coupon.ExpiresAt)
        {
            _logger.LogInformation("Coupon {Code} expired at {Expiry}", coupon.Code, coupon.ExpiresAt);
            return false;
        }
        return true;
    }

    public TimeSpan TimeUntilExpiry(Coupon coupon)
    {
        var remaining = coupon.ExpiresAt - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

public class Coupon
{
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
