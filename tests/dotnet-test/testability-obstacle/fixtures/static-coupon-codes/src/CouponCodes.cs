namespace Coupons;

public static class CouponCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Create(int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return string.Create(length, 0, static (span, _) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = Alphabet[Random.Shared.Next(Alphabet.Length)];
            }
        });
    }
}
