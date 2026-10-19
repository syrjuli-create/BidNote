namespace BidNote.Application.Home;

public sealed record NoticeListItem(
    int Id, string BidNo, string Name, string Agency, string Region, string License,
    long? BasePrice, DateTime PostedAt, DateTime DueAt, int CorrectionCount);

public sealed record HomeSummary(
    int ActiveCount,
    int ClosingSoonCount,
    int NewCount,
    int CorrectedCount,
    IReadOnlyList<NoticeListItem> ClosingSoon,
    IReadOnlyList<NoticeListItem> RecentlyCorrected,
    DateTime? LastCollectedAt);

public enum NoticeSort
{
    DueSoon,
    AmountHigh,
    Newest,
}

public enum AmountRange
{
    Any,
    Under100M,
    From100MTo500M,
    Over500M,
}

/// <summary>공고 목록 조건. 비어 있는 조건은 걸지 않는다.</summary>
public sealed record NoticeFilter
{
    public string? Keyword { get; init; }
    public IReadOnlyList<string> Regions { get; init; } = [];
    public string? License { get; init; }
    public AmountRange Amount { get; init; }

    /// <summary>며칠 안에 마감되는 공고만. null이면 전체.</summary>
    public int? DueWithinDays { get; init; }

    public bool CorrectedOnly { get; init; }
    public bool NewOnly { get; init; }
    public NoticeSort Sort { get; init; }
}

public sealed record FacetCount(string Value, int Count);

public sealed record NoticeSearchResult(
    IReadOnlyList<NoticeListItem> Items,
    IReadOnlyList<FacetCount> Regions,
    IReadOnlyList<FacetCount> Licenses);

public interface IHomeQueries
{
    /// <summary>메인 화면에 보여 줄 요약. 마감이 지나지 않은 공고만 센다.</summary>
    Task<HomeSummary> GetSummaryAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>마감이 지나지 않은 공고를 조건대로 찾는다. 지역·업종 목록은 조건과 상관없이 전체 기준이다.</summary>
    Task<NoticeSearchResult> SearchAsync(NoticeFilter filter, DateTime now, CancellationToken cancellationToken);
}

public static class HomeRules
{
    /// <summary>"마감 임박" = 내일 자정 전까지 마감되는 공고.</summary>
    public static DateTime ClosingSoonLimit(DateTime now) => now.Date.AddDays(2);

    /// <summary>"새 공고"로 보는 공고일 범위.</summary>
    public static readonly TimeSpan NewWindow = TimeSpan.FromDays(5);

    public const int ListSize = 5;

    /// <summary>목록 한 번에 보여 줄 최대 건수.</summary>
    public const int SearchLimit = 200;
}
