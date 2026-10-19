namespace BidNote.Domain.Notices;

/// <summary>공고가 바뀌기 직전 상태의 스냅샷.</summary>
public class NoticeHistory
{
    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public string BidSeq { get; set; } = "";
    public NoticeChangeType ChangeType { get; set; }

    /// <summary>바뀌기 전 공고 내용(JSON).</summary>
    public string SnapshotJson { get; set; } = "";

    public string RawHash { get; set; } = "";
    public DateTime DetectedAt { get; set; }
}
