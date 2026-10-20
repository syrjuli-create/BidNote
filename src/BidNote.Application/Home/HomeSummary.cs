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

    /// <summary>계약방법 묶음 하나만 (예: 제한경쟁). 비우면 전체.</summary>
    public string? ContractMethod { get; init; }

    /// <summary>수의계약(소액 견적 등)을 뺀다.</summary>
    public bool ExcludeNegotiated { get; init; }
    public bool NewOnly { get; init; }
    public NoticeSort Sort { get; init; }

    /// <summary>1부터 시작하는 페이지 번호.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = HomeRules.PageSize;
}

public sealed record FacetCount(string Value, int Count);

public sealed record NoticeSearchResult(
    IReadOnlyList<NoticeListItem> Items,
    IReadOnlyList<FacetCount> Regions,
    IReadOnlyList<FacetCount> Licenses)
{
    public IReadOnlyList<FacetCount> ContractMethods { get; init; } = [];
    /// <summary>조건에 맞는 전체 건수 (이 페이지만이 아니라).</summary>
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = HomeRules.PageSize;
    public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}

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

    /// <summary>목록 한 페이지 건수.</summary>
    public const int PageSize = 50;

    /// <summary>한 페이지에 보여 줄 수 있는 최대 건수.</summary>
    public const int MaxPageSize = 200;
}
