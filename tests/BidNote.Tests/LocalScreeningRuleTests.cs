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
        Assert.Equal(("80% 이상", "20% 미만"), (b4.Grades[1].RatioText(0), b4.Grades[1].RatioText(7)));   // 전문·그 밖의 공사는 80% 이상이 A

        var b5 = LocalScreeningRules.BandFor(5 * Eok)!;    // [별표 5] 10억 미만 4억 이상
        Assert.Equal((80, "70% 이상", "60% 이상", 10.0), (b5.PricePoints, b5.Grades[0].RatioText(0), b5.Grades[0].RatioText(0, shortPeriod: true), b5.Grades[0].Grades[0].Points));

        var b6 = LocalScreeningRules.BandFor(3 * Eok)!;    // [별표 6] 4억 미만 2억 이상
        Assert.Equal([4.8, 5.0], b6.Grades.Select(t => t.Grades[0].Points));
    }

    [Theory]
    [InlineData(20, 100, true, 15.0, "A")]   // 별표 4, 종합: 100% 이상 A
    [InlineData(20, 85, true, 11.0, "C")]    // 80% 이상 C
    [InlineData(20, 85, false, 15.0, "A")]   // 전문은 80% 이상이 A
    [InlineData(20, 30, true, 1.0, "H")]
    public void 지자체_10억_30억_구간은_5년_실적_비율_등급으로_점수를_준다(double eok, double ratio, bool general, double points, string grade)
    {
        long price = (long)(eok * Eok);
        var e = LocalScreeningRules.EstimateExperience(LocalScreeningRules.BandFor(price), price, general, (long)(price * ratio / 100), null, false);

        Assert.NotNull(e);
        Assert.Equal((points, grade, 15.0), (e.Points, e.Grade, e.MaxPoints));
    }

    [Fact]
    public void 지자체_10억_미만은_3년_실적을_쓰고_등록_4년_미만이면_기준이_낮다()
    {
        long price = 5 * Eok;   // 별표 5
        var band = LocalScreeningRules.BandFor(price);

        Assert.Equal(8.0, LocalScreeningRules.EstimateExperience(band, price, true, null, (long)(price * 0.55), false)!.Points);  // 50% 이상 C
        Assert.Equal(9.0, LocalScreeningRules.EstimateExperience(band, price, true, null, (long)(price * 0.55), true)!.Points);   // 4년 미만: 50% 이상 B
        Assert.Null(LocalScreeningRules.EstimateExperience(band, price, true, 5 * Eok, null, false));                          // 3년 실적이 없으면 계산하지 않는다
    }

    [Fact]
    public void 지자체_2억_미만은_4억_구간_점수를_4점_만점으로_줄이고_50억_이상은_실적계수로_본다()
    {
        long small = Eok;
        var below2 = LocalScreeningRules.EstimateExperience(LocalScreeningRules.BandFor(small), small, true, null, small, false)!;
        Assert.Equal((4.0, 4.0), (below2.Points, below2.MaxPoints));   // 종합 A 4.8 × 4/4.8

        long big = 60 * Eok;   // 별표 2: 15 × min(1, 3년 실적 ÷ (추정가격 × 1.7))
        var e = LocalScreeningRules.EstimateExperience(LocalScreeningRules.BandFor(big), big, true, null, (long)(big * 0.85), false)!;
        Assert.Equal(7.5, e.Points);
        Assert.Null(LocalScreeningRules.EstimateExperience(LocalScreeningRules.BandFor(150 * Eok), 150 * Eok, true, Eok, Eok, false)); // 별표 1은 계산 안 함
    }

    [Theory]
    [InlineData("토목공사업", true)]
    [InlineData("토목건축공사업", true)]
    [InlineData("산업ㆍ환경설비공사업", true)]
    [InlineData("지반조성ㆍ포장공사업", false)]
    [InlineData("전기공사업", false)]
    public void 종합공사업과_전문을_면허_이름으로_나눈다(string name, bool general) =>
        Assert.Equal(general, LocalScreeningRules.IsGeneralLicense(name));

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
