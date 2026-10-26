namespace BidNote.Application.Bidding;

public sealed record MyBidInput(int NoticeId, double AssessmentRate, long BidAmount, string Memo = "");

public sealed record MyBidView(int NoticeId, double AssessmentRate, long BidAmount, long? BasePriceAtBid, long? AValueAtBid, string Memo, DateTime UpdatedAt);

/// <summary>투찰 기록이 어느 단계인지. 개찰 결과를 받으면 단계가 더 늘어난다.</summary>
public enum MyBidStage
{
    /// <summary>마감 전. 아직 고칠 수 있다.</summary>
    BeforeDue,

    /// <summary>마감했고 개찰 전.</summary>
    WaitingOpen,

    /// <summary>개찰 시각이 지났다.</summary>
    Opened,

    /// <summary>공고가 취소됐다.</summary>
    Cancelled,
}

public sealed record MyBidItem(
    int NoticeId, string BidNo, string Name, string Agency, string Region, string License,
    long? BasePrice, DateTime DueAt, DateTime OpenAt, bool IsActive,
    double AssessmentRate, long BidAmount, long? BasePriceAtBid, string Memo, DateTime UpdatedAt)
{
    public MyBidStage StageAt(DateTime now) =>
        !IsActive ? MyBidStage.Cancelled
        : now < DueAt ? MyBidStage.BeforeDue
        : now < OpenAt ? MyBidStage.WaitingOpen
        : MyBidStage.Opened;

    /// <summary>기록한 뒤 기초금액이 바뀌었는지 (정정 공고 등).</summary>
    public bool BasePriceChanged => BasePriceAtBid is not null && BasePrice is not null && BasePriceAtBid != BasePrice;
}

public interface IMyBidStore
{
    /// <summary>
    /// 투찰 기록을 저장한다. 이미 있으면 고친다. 사정률·금액이 0 이하이면 예외.
    /// 관심 공고(휴지통 제외)에 담긴 공고면 '투찰 완료' 폴더로 옮기고, 옮겼으면 true를 돌려준다.
    /// </summary>
    Task<bool> SaveAsync(MyBidInput input, CancellationToken cancellationToken);

    Task RemoveAsync(int noticeId, CancellationToken cancellationToken);

    Task<MyBidView?> GetAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>마감 전 기록은 마감 임박순, 나머지는 최근 개찰순.</summary>
    Task<IReadOnlyList<MyBidItem>> ListAsync(DateTime now, CancellationToken cancellationToken);
}
