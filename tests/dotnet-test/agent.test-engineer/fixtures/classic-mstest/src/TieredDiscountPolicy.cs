using System;

namespace Contoso.Discounts
{
    public sealed class TieredDiscountPolicy
    {
        private readonly decimal _threshold;
        private readonly decimal _rate;

        public TieredDiscountPolicy(decimal threshold, decimal rate)
        {
            if (threshold <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(threshold));
            }

            if (rate < 0m || rate > 1m)
            {
                throw new ArgumentOutOfRangeException(nameof(rate));
            }

            _threshold = threshold;
            _rate = rate;
        }

        public decimal Apply(decimal subtotal)
        {
            if (subtotal < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(subtotal));
            }

            return subtotal >= _threshold
                ? subtotal * (1m - _rate)
                : subtotal;
        }
    }
}
