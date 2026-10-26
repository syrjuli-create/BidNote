using BidNote.Domain.Bidding;

namespace BidNote.Tests;

public class LocalScreeningRuleTests
{
    private const long Eok = 100_000_000;

    [Theory]
    [InlineData(1.9, "별표 7")]
    [InlineData(2, "별표 6")]
    [InlineData(3.9, "별표 6")]
    [InlineData(4, "별표 5")]
    [InlineData(10, "별표 4")]
    [InlineData(30, "별표 3")]
    [InlineData(50, "별표 2")]
    [InlineData(100, "별표 1")]
    public void 추정가격으로_행안부_적격심사_구간을_고른다(double eok, string appendix) =>
        Assert.Equal(appendix, LocalScreeningRules.BandFor((long)(eok * Eok))!.Appendix);

    [Fact]
    public void 삼백억_이상이거나_추정가격을_모르면_구간이_없다()
    {
        Assert.Null(LocalScreeningRules.BandFor(300 * Eok));
        Assert.Null(LocalScreeningRules.BandFor(null));
    }

    [Fact]
    public void 원문_별표의_배점과_등급표를_옮겼다()
    {
        var b4 = LocalScreeningRules.BandFor(20 * Eok)!;   // [별표 4] 30억 미만 10억 이상
        Assert.Equal((30, 70, "최근 5년"), (b4.CapabilityPoints, b4.PricePoints, b4.Period));
        Assert.Equal([15.0, 13, 11, 9, 7, 5, 3, 1], b4.Grades[0].Grades.Select(g => g.Points));
        Assert.Equal("80% 이상", b4.Grades[1].Grades[0].Ratio);   // 전문·그 밖의 공사는 80% 이상이 A

        var b5 = LocalScreeningRules.BandFor(5 * Eok)!;    // [별표 5] 10억 미만 4억 이상
        Assert.Equal((80, "70% 이상", "60% 이상", 10.0), (b5.PricePoints, b5.Grades[0].Grades[0].Ratio, b5.Grades[0].Grades[0].ShortRatio, b5.Grades[0].Grades[0].Points));

        var b6 = LocalScreeningRules.BandFor(3 * Eok)!;    // [별표 6] 4억 미만 2억 이상
        Assert.Equal([4.8, 5.0], b6.Grades.Select(t => t.Grades[0].Points));
    }

    [Theory]
    [InlineData("경상북도 경주시", true)]
    [InlineData("서울특별시 물재생센터 중랑물재생센터", true)]
    [InlineData("대구광역시교육청 대구광역시남부교육지원청", true)]
    [InlineData("경기도교육청 경안고등학교", true)]
    [InlineData("강원특별자치도", true)]
    [InlineData("○○시", true)]
    [InlineData("조달청 부산지방조달청", false)]
    [InlineData("국토교통부 서울지방국토관리청", false)]
    [InlineData("한국도로공사", false)]
    [InlineData("주식회사킨텍스", false)]
    [InlineData("", false)]
    public void 공고기관_이름으로_지자체_발주인지_본다(string agency, bool local) =>
        Assert.Equal(local, LocalScreeningRules.IsLocalAgency(agency));
}
