using BidNote.Domain.Notices;

namespace BidNote.Domain.Favorites;

/// <summary>관심 공고. 공고 하나에 한 줄만 둔다.</summary>
public class FavoriteNotice
{
    public const int MaxMemoLength = 500;

    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public string Memo { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
