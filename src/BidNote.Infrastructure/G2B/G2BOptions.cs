namespace BidNote.Infrastructure.G2B;

public sealed class G2BOptions
{
    public const string SectionName = "G2B";

    /// <summary>공공데이터포털에서 받은 서비스 키(Decoding). 실제 키는 appsettings.Local.json에만 둔다.</summary>
    public string ServiceKey { get; set; } = "";

    /// <summary>true면 실제 API 대신 가상 공고를 만든다 (API 키를 받기 전 개발용).</summary>
    public bool UseFakeData { get; set; } = true;

    /// <summary>입찰공고정보서비스 주소 (차세대 나라장터).</summary>
    public string BaseUrl { get; set; } = "https://apis.data.go.kr/1230000/ad/BidPublicInfoService/";

    /// <summary>낙찰정보서비스 주소 (개찰 결과). 같은 키로 활용 신청한다. `/as/` 경로만 동작한다 (2026-10-25 확인).</summary>
    public string AwardBaseUrl { get; set; } = "https://apis.data.go.kr/1230000/as/ScsbidInfoService/";

    /// <summary>한 번에 받는 건수. 999까지 동작하는 것을 확인했다 (2026-10-19).</summary>
    public int PageSize { get; set; } = 999;

    /// <summary>한 번 수집할 때 API 하나에 넘길 최대 페이지 수. 60일치 면허(약 86페이지)까지 받을 수 있게 둔다.</summary>
    public int MaxPagesPerRun { get; set; } = 100;
}
