using BidNote.Domain.Notices;

namespace BidNote.Application.Notices;

public sealed record LabeledValue(string Label, string Value);

public sealed record FieldChange(string Label, string Before, string After);

public sealed record NoticeChange(DateTime DetectedAt, NoticeChangeType Type, string FromSeq, string ToSeq, IReadOnlyList<FieldChange> Fields);

/// <summary>면허 제한 한 그룹. 그룹 안의 면허는 "또는", 그룹끼리는 "그리고"로 본다.</summary>
public sealed record LicenseGroup(int GroupNo, IReadOnlyList<string> Names);

/// <summary>기초금액 원문의 비용 항목 하나. InAValue면 A값에 더한 항목이다.</summary>
public sealed record CostItem(string Label, long Amount, bool InAValue);

public sealed record NoticeDetail
{
    public required int Id { get; init; }
    public required string SourceSystem { get; init; }
    public required string BidNo { get; init; }
    public required string BidSeq { get; init; }
    public required string Name { get; init; }
    public required string Agency { get; init; }
    public string DemandAgency { get; init; } = "";
    public string Region { get; init; } = "";
    public string SiteRegionName { get; init; } = "";
    public string License { get; init; } = "";
    public long? BasePrice { get; init; }
    public DateTime? BasePriceOpenedAt { get; init; }
    public string ReservePriceRange { get; init; } = "";
    public long? AValue { get; init; }
    public long? NetCost { get; init; }
    public double LowerLimitRate { get; init; }
    public DateTime PostedAt { get; init; }
    public DateTime DueAt { get; init; }
    public DateTime OpenAt { get; init; }
    public bool IsActive { get; init; }
    public bool IsRebid { get; init; }
    public int CorrectionCount { get; init; }
    public string DetailUrl { get; init; } = "";
    public DateTime LastCheckedAt { get; init; }
    public IReadOnlyList<LicenseGroup> Licenses { get; init; } = [];

    /// <summary>A값을 이루는 항목과 A값에 넣지 않는 비용 항목. 기초금액 원문이 없으면 비어 있다.</summary>
    public IReadOnlyList<CostItem> CostItems { get; init; } = [];

    /// <summary>정정 이력. 최근 것이 앞에 온다.</summary>
    public IReadOnlyList<NoticeChange> Changes { get; init; } = [];

    /// <summary>원문에서 꺼낸 추가 정보 (예산금액, 계약방법 등). 원문 형식을 모르면 비어 있다.</summary>
    public IReadOnlyList<LabeledValue> Extras { get; init; } = [];
}

public interface INoticeDetailQueries
{
    Task<NoticeDetail?> GetAsync(int id, CancellationToken cancellationToken);
}
