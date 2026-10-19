namespace BidNote.Application.Collecting;

/// <summary>전자조달 시스템 하나에서 공고를 받아 오는 수집기 (나라장터, 국방전자조달 등).</summary>
public interface IBidCollector
{
    string SourceSystem { get; }

    /// <summary>변경 일시가 [from, to] 사이인 공고를 받아 온다.</summary>
    Task<IReadOnlyList<CollectedNotice>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>[from, to] 사이에 공개된 기초금액을 받아 온다. 공고보다 늦게 공개되므로 따로 받는다.</summary>
    Task<IReadOnlyList<CollectedBasePrice>> CollectBasePricesAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}
