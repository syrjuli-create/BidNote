namespace BidNote.Application.Collecting;

/// <summary>공고 하나(공고번호+차수)의 참가 가능 지역. 원문 여러 줄을 합친 것이다.</summary>
public sealed record CollectedRegionLimit(string BidNo, string BidSeq, IReadOnlyList<string> Regions, string RawJson, string ApiName, string RequestParameter);

/// <summary>참가 가능 지역을 받아 오는 곳 (나라장터 입찰공고정보서비스).</summary>
public interface IRegionLimitSource
{
    string SourceSystem { get; }

    /// <summary>[from, to] 사이에 등록된 공사 공고의 참가 가능 지역.</summary>
    Task<IReadOnlyList<CollectedRegionLimit>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}

public interface IRegionLimitService
{
    /// <summary>참가 가능 지역을 한 번이라도 받은 적이 있는지. 처음이면 지난 기간을 함께 받는다.</summary>
    Task<bool> HasAnyAsync(CancellationToken cancellationToken);

    /// <summary>참가 가능 지역을 받아 저장된 공고에 붙인다. 새로 붙이거나 바뀐 공고 수를 돌려준다.</summary>
    Task<int> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>진행 공고(마감 전·취소 아님) 중 가장 먼저 올라온 공고의 게시일. 진행 공고가 없으면 null.</summary>
    Task<DateTime?> OldestOpenPostedAtAsync(DateTime now, CancellationToken cancellationToken);
}
