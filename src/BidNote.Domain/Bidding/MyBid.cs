using BidNote.Domain.Notices;

namespace BidNote.Domain.Bidding;

/// <summary>내가 써낸(또는 써낼) 투찰 한 건. 공고 하나에 한 줄만 두고, 다시 저장하면 고친다.</summary>
public class MyBid
{
    public const int MaxMemoLength = 500;

    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    /// <summary>사정률(%).</summary>
    public double AssessmentRate { get; set; }

    /// <summary>투찰금액(원).</summary>
    public long BidAmount { get; set; }

    /// <summary>기록할 때의 기초금액. 나중에 기초금액이 바뀌어도 어떤 값으로 계산했는지 남긴다.</summary>
    public long? BasePriceAtBid { get; set; }

    /// <summary>기록할 때의 A값.</summary>
    public long? AValueAtBid { get; set; }

    public string Memo { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
