using BidNote.Application.Company;

namespace BidNote.Tests;

public class LicenseNameSuggestionTests
{
    private static readonly string[] Known =
    [
        "전기공사업", "정보통신공사업", "토목공사업", "토목건축공사업", "지반조성ㆍ포장공사업", "실내건축공사업", "건축공사업",
    ];

    [Theory]
    [InlineData("전기공사", "전기공사업")]
    [InlineData("지반조성 포장공사업", "지반조성ㆍ포장공사업")]   // 가운뎃점·띄어쓰기만 다름
    [InlineData("지반조성·포장공사업", "지반조성ㆍ포장공사업")]   // 다른 가운뎃점
    [InlineData("정보통신", "정보통신공사업")]
    [InlineData("실내건축공사", "실내건축공사업")]
    public void 가까운_면허_이름을_맨_앞에_제안한다(string input, string expected) =>
        Assert.Equal(expected, LicenseNameSuggestions.For(input, Known).First());

    [Fact]
    public void 입력이_들어_있는_이름은_짧은_것부터_최대_3개()
    {
        Assert.Equal(["토목공사업", "토목건축공사업"], LicenseNameSuggestions.For("토목", Known));
        Assert.Equal(3, LicenseNameSuggestions.For("공사업", Known).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("소방시설공사업")]
    public void 비었거나_비슷한_이름이_없으면_제안하지_않는다(string input) =>
        Assert.Empty(LicenseNameSuggestions.For(input, Known));
}
