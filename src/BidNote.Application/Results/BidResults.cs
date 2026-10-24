using BidNote.Domain.Results;

namespace BidNote.Application.Results;

/// <summary>개찰 목록 한 줄. 같은 공고가 분류·재입찰 번호별로 여러 줄 올 수 있다.</summary>
public sealed record CollectedOpening
{
    public required string BidNo { get; init; }
    public required string BidSeq { get; init; }
    public string ClassNo { get; init; } = "";
    public string RebidNo { get; init; } = "";
    public string Status { get; init; } = "";
    public int ParticipantCount { get; init; }
    public DateTime? OpenedAt { get; init; }
    public string WinnerName { get; init; } = "";
    public string WinnerBizNo { get; init; } = "";
    public long? WinnerAmount { get; init; }
    public double? WinnerBidRate { get; init; }
    public string ApiName { get; init; } = "";
    public string RequestParameter { get; init; } = "";
    public string RawJson { get; init; } = "";
}

public sealed record CollectedRank(int? Rank, string CompanyName, string BizNo, long? Amount, double? BidRate, string Remark);

/// <summary>공고 하나의 업체별 순위와 예정가격. Complete가 false면 한도 등으로 순위를 일부만 받았다.</summary>
public sealed record CollectedRanking(long? PlannedPrice, long? BasePrice, IReadOnlyList<CollectedRank> Ranks, bool Complete);

/// <summary>개찰 결과를 받아 오는 곳 (나라장터 낙찰정보서비스).</summary>
public interface IBidResultSource
{
    string SourceSystem { get; }

    /// <summary>개찰 일시가 [from, to] 사이인 개찰 목록.</summary>
    Task<IReadOnlyList<CollectedOpening>> CollectOpeningsAsync(DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>공고 하나의 업체별 순위와 예정가격. 받을 수 없으면 null.</summary>
    Task<CollectedRanking?> FetchRankingAsync(string bidNo, string bidSeq, string classNo, string rebidNo, CancellationToken cancellationToken);
}

public sealed record BidRankView(int? Rank, string CompanyName, long? Amount, double? BidRate, double? AssessmentRate, string Remark);

/// <summary>내 투찰 기록과 개찰 결과를 견준 것. 사정률 차이는 내 사정률 − 1순위 사정률.</summary>
public sealed record MyBidOutcome(
    int NoticeId, string Status, int ParticipantCount, double? ActualRate,
    double? WinnerAssessmentRate, double? RateGap, MyRank Rank);

public sealed record BidResultView(
    int NoticeId, string Status, int ParticipantCount, DateTime? OpenedAt,
    long? PlannedPrice, double? ActualRate, long? LowerLimitAmount,
    BidRankView? Winner, IReadOnlyList<BidRankView> Top, int RankedCount, int TotalCount,
    DateTime? RanksFetchedAt, bool RanksComplete, MyBidOutcome? Mine)
{
    public bool IsOpened => Status.Contains("개찰완료", StringComparison.Ordinal);

    /// <summary>순위를 받아 올 차례인지 (개찰 완료, 참가 업체 있음, 아직 안 받음).</summary>
    public bool NeedsRanks => IsOpened && ParticipantCount > 0 && RanksFetchedAt is null;
}

public interface IBidResultService
{
    /// <summary>개찰 결과가 하나라도 저장돼 있는지. 처음이면 지난 기간을 함께 받는다.</summary>
    Task<bool> HasAnyAsync(CancellationToken cancellationToken);

    /// <summary>개찰 목록을 받아 저장된 공고에 붙인다. 새로 붙이거나 바뀐 결과 수를 돌려준다.</summary>
    Task<int> CollectOpeningsAsync(DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>공고 하나의 업체별 순위와 예정가격을 받아 저장한다. 받을 결과가 없으면 false.</summary>
    Task<bool> FetchRanksAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>내 투찰 기록·관심 공고 중 순위를 아직 받지 않은 것을 최대 <paramref name="max"/>건 받는다.</summary>
    Task<int> FetchPendingRanksAsync(int max, CancellationToken cancellationToken);

    /// <summary>상세 화면용 개찰 결과. 순위는 앞에서 <paramref name="top"/>개만 준다. 결과가 없으면 null.</summary>
    Task<BidResultView?> GetAsync(int noticeId, int top, CancellationToken cancellationToken);

    /// <summary>개찰 결과가 있는 내 투찰 기록마다의 비교 결과.</summary>
    Task<IReadOnlyDictionary<int, MyBidOutcome>> MyBidOutcomesAsync(CancellationToken cancellationToken);
}
