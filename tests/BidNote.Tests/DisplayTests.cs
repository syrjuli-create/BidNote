using BidNote.Web;

namespace BidNote.Tests;

public class DisplayTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 8, 47, 0);

    [Theory]
    [InlineData(2026, 10, 19, 17, "D-day")]
    [InlineData(2026, 10, 20, 10, "D-1")] // 내일 오전 마감은 남은 시간이 25시간이어도 D-1
    [InlineData(2026, 10, 23, 10, "D-4")]
    public void 마감_D_day는_날짜_기준으로_센다(int y, int m, int d, int h, string expected)
    {
        Assert.Equal(expected, Display.DDay(new DateTime(y, m, d, h, 0, 0), Now));
    }

    [Theory]
    [InlineData(186_400_000L, "1.86억 원")]
    [InlineData(94_300_000L, "9,430만 원")]
    [InlineData(1_000_000_000L, "10억 원")]
    public void 금액은_억_만_단위로_보여준다(long amount, string expected)
    {
        Assert.Equal(expected, Display.Eok(amount));
    }
}
