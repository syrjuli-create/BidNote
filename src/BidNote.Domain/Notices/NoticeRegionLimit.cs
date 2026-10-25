namespace BidNote.Domain.Notices;

/// <summary>
/// 공고의 참가 가능 지역 한 줄 (나라장터 getBidPblancListInfoPrtcptPsblRgn). 시도("충청북도")나 시군구("충청북도 충주시") 이름이다.
/// 줄이 여러 개면 그중 한 곳에 본사나 참여 지사가 있으면 된다 (2026-10-25 실데이터: 충주시·제천시·단양군처럼 온다).
/// </summary>
public class NoticeRegionLimit
{
    public const int MaxNameLength = 100;

    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public string Name { get; set; } = "";
}
