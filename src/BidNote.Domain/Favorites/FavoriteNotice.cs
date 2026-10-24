using BidNote.Domain.Notices;

namespace BidNote.Domain.Favorites;

/// <summary>관심 공고를 정리하는 폴더. 숫자는 DB에 그대로 저장되므로 바꾸지 않는다.</summary>
public enum FavoriteFolder
{
    Reviewing = 0,
    Planned = 1,
    Submitted = 2,
}

/// <summary>관심 공고. 공고 하나에 한 줄만 둔다. 빼면 지우지 않고 휴지통(TrashedAt)으로 보낸다.</summary>
public class FavoriteNotice
{
    public const int MaxMemoLength = 500;

    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public string Memo { get; set; } = "";
    public FavoriteFolder Folder { get; set; } = FavoriteFolder.Reviewing;

    /// <summary>휴지통에 넣은 시각. null이면 휴지통에 없다.</summary>
    public DateTime? TrashedAt { get; set; }

    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
