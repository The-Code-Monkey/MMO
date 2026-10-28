using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests.EditMode
{
    public sealed class TaxationRulesTests
    {
        [Test]
        public void ClampTax_FencesOverCeiling()
        {
            Assert.AreEqual(0, TaxationRules.ClampTax(-1));
            Assert.AreEqual(0, TaxationRules.ClampTax(0));
            Assert.AreEqual(200, TaxationRules.ClampTax(200));
            Assert.AreEqual(TaxationRules.MaxTaxRateBp, TaxationRules.ClampTax(1200));
        }

        [Test]
        public void TaxOn_RoundsToCurrencies()
        {
            // 100_000 @ 200bp (2%) -> 2_000
            Assert.AreEqual(2_000, TaxationRules.TaxOn(100_000, 200));
        }

        [Test]
        public void TaxOn_ZeroForZeroRateOrValue()
        {
            Assert.AreEqual(0, TaxationRules.TaxOn(100_000, 0));
            Assert.AreEqual(0, TaxationRules.TaxOn(0, 200));
            Assert.AreEqual(0, TaxationRules.TaxOn(-5, 200));
        }

        [Test]
        public void PaidByPayer_IsNetOfTax()
        {
            // Payer sees value minus tax; 100_000 @ 200bp -> 98_000
            Assert.AreEqual(98_000, TaxationRules.PaidByPayer(100_000, 200));
        }

        [Test]
        public void PaidByPayer_FullValueAtZeroRate()
            => Assert.AreEqual(100_000, TaxationRules.PaidByPayer(100_000, 0));

        [Test]
        public void PaidByPayer_ClampsAtZeroForTinyValue()
            => Assert.AreEqual(0, TaxationRules.PaidByPayer(1, 500));
    }
}
