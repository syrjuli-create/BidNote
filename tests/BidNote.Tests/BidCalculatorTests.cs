using BidNote.Domain.Bidding;

namespace BidNote.Tests;

public class BidCalculatorTests
{
    [Fact]
    public void A값이_있으면_A값_산식으로_계산한다()
    {
        // {(186,400,000 × 100%) − 7,420,000} × 87.745% + 7,420,000 = 164,466,001 (올림)
        var r = BidCalculator.Calculate(new BidInput(186_400_000, 100, 87.745, AValue: 7_420_000, NetCost: 152_300_000));

        Assert.Equal(164_466_001, r.BidAmount);
        Assert.Equal(186_400_000m, r.PlannedPrice);
        Assert.Equal(149_254_000m, r.NetCostFloor);
        Assert.False(r.UsedNetCostFloor);
    }

    [Fact]
    public void A값이_없으면_기본_산식으로_계산한다()
    {
        var r = BidCalculator.Calculate(new BidInput(100_000_000, 99.5, 87.745));

        Assert.Null(r.AValueAmount);
        Assert.Equal(87_306_275, r.BidAmount); // 100,000,000 × 99.5% × 87.745%
    }

    [Fact]
    public void 순공사원가_하한이_더_높으면_하한_금액을_쓴다()
    {
        var r = BidCalculator.Calculate(new BidInput(100_000_000, 100, 87.745, NetCost: 99_000_000));

        Assert.True(r.UsedNetCostFloor);
        Assert.Equal(97_020_000, r.BidAmount); // 99,000,000 × 100% × 98%
    }

    [Fact]
    public void 투찰금액으로_사정률을_거꾸로_계산한다()
    {
        var r = BidCalculator.Calculate(new BidInput(186_400_000, 99.8765, 87.745, AValue: 7_420_000));

        double rate = BidCalculator.AssessmentRateFor(186_400_000, 87.745, 7_420_000, r.BidAmount);

        Assert.Equal(99.8765, rate, precision: 4);
    }

    [Theory]
    [InlineData(0, 100, 87.745)]
    [InlineData(1000, 0, 87.745)]
    [InlineData(1000, 100, 0)]
    public void 기초금액_사정률_하한율이_0이면_예외(long basePrice, double rate, double low)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BidCalculator.Calculate(new BidInput(basePrice, rate, low)));
    }
}
