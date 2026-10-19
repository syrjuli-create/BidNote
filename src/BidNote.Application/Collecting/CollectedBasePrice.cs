using BidNote.Domain.Notices;

namespace BidNote.Application.Collecting;

/// <summary>공고 하나의 기초금액과 투찰금액 계산에 필요한 값.</summary>
public sealed record CollectedBasePrice
{
    public required string SourceSystem { get; init; }
    public required BusinessType BusinessType { get; init; }
    public required string BidNo { get; init; }
    public required string BidSeq { get; init; }

    public required long BasePrice { get; init; }

    /// <summary>A값. 이 공고에 A값 산식을 쓰지 않으면 null.</summary>
    public long? AValue { get; init; }

    public long? NetCost { get; init; }
    public DateTime? OpenedAt { get; init; }
    public string ReservePriceRange { get; init; } = "";

    public required string ApiName { get; init; }
    public required string RequestParameter { get; init; }
    public required string RawJson { get; init; }
}
