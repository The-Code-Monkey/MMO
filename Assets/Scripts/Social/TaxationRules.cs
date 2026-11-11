using System;

namespace MMO.Social
{
    /// <summary>
    /// Pure territory taxation: the basis-point clamp, the tax on a value, and the payer's net.
    /// No backend or Unity dependency, so the ceiling and rounding are unit-testable. The tax is
    /// applied to the *seller* side of a trade or auction inside a claimed territory.
    /// </summary>
    public static class TaxationRules
    {
        /// <summary>The highest settable rate: 500 bp = 5%.</summary>
        public const int MaxTaxRateBp = 500;

        /// <summary>Fences a rate into [0, <see cref="MaxTaxRateBp"/>].</summary>
        public static int ClampTax(int bp) => Math.Max(0, Math.Min(MaxTaxRateBp, bp));

        /// <summary>The tax on <paramref name="value"/> at <paramref name="bp"/> basis points, rounding up.</summary>
        public static long TaxOn(long value, int bp)
        {
            if (value <= 0 || bp <= 0)
                return 0;

            return (value * bp + 9_999) / 10_000;
        }

        /// <summary>What the payer keeps after the tax: value minus <see cref="TaxOn"/>.</summary>
        public static long PaidByPayer(long value, int bp)
        {
            if (value <= 0)
                return 0;

            return value - TaxOn(value, bp);
        }
    }
}
