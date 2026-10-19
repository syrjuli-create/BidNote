using BidNote.Domain.Notices;

namespace BidNote.Application.Collecting;

public sealed record CollectedLicense(int GroupNo, string Name);

/// <summary>수집기가 API 응답을 해석해 넘기는 공고 한 건과 그 원문.</summary>
public sealed record CollectedNotice
{
    public required string SourceSystem { get; init; }
    public required BusinessType BusinessType { get; init; }
    public required string BidNo { get; init; }
    public required string BidSeq { get; init; }
    public required string Name { get; init; }
    public required string Agency { get; init; }
    public string DemandAgency { get; init; } = "";
    public string Region { get; init; } = "";
    public string SiteRegionName { get; init; } = "";
    public string License { get; init; } = "";
    public IReadOnlyList<CollectedLicense> Licenses { get; init; } = [];
    public string DetailUrl { get; init; } = "";
    public long? BasePrice { get; init; }
    public double LowerLimitRate { get; init; }
    public long? AValue { get; init; }
    public long? NetCost { get; init; }
    public required DateTime PostedAt { get; init; }
    public required DateTime DueAt { get; init; }
    public required DateTime OpenAt { get; init; }
    public bool IsRebid { get; init; }
    public bool IsCancelled { get; init; }
    public required DateTime LastModifiedAt { get; init; }

    public required string ApiName { get; init; }
    public required string RequestParameter { get; init; }
    public required string RawJson { get; init; }
    public int HttpStatus { get; init; } = 200;
}
