namespace BidNote.Domain.Notices;

public enum BusinessType
{
    Construction = 1,
    Service = 2,
    Goods = 3,
}

public enum NoticeChangeType
{
    /// <summary>공고 차수가 올라간 정정 공고.</summary>
    Corrected = 1,

    /// <summary>재공고.</summary>
    Rebid = 2,

    /// <summary>취소 공고.</summary>
    Cancelled = 3,

    /// <summary>차수는 같지만 내용이 바뀜 (기초금액 공개 등).</summary>
    Updated = 4,
}

public static class SourceSystems
{
    public const string G2B = "G2B";
    public const string D2B = "D2B";
    public const string Kepco = "KEPCO";
    public const string Lh = "LH";

    /// <summary>API 키를 받기 전 화면 개발용 가상 공고. 실제 모드로 바꾸면 지운다.</summary>
    public const string Fake = "FAKE";
}
