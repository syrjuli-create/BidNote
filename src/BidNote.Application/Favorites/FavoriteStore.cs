using BidNote.Domain.Favorites;

namespace BidNote.Application.Favorites;

public sealed record FavoriteItem(
    int NoticeId, string BidNo, string Name, string Agency, string Region, string License,
    long? BasePrice, DateTime DueAt, bool IsActive, int CorrectionCount, string Memo, DateTime AddedAt,
    FavoriteFolder Folder, DateTime? TrashedAt);

/// <summary>Open·Closed는 휴지통에 없는 것, Trash는 휴지통에 있는 것.</summary>
public sealed record FavoriteList(
    IReadOnlyList<FavoriteItem> Open, IReadOnlyList<FavoriteItem> Closed, IReadOnlyList<FavoriteItem> Trash);

public interface IFavoriteStore
{
    /// <summary>담겨 있으면 휴지통으로 보내고, 없거나 휴지통에 있으면 담는다(휴지통에서는 원래 폴더로 복원). 바뀐 뒤의 상태(담겼으면 true)를 돌려준다.</summary>
    Task<bool> ToggleAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>휴지통으로 보낸다. 메모와 폴더는 그대로 둔다.</summary>
    Task TrashAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>휴지통에서 꺼내 원래 폴더로 되돌린다.</summary>
    Task RestoreAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>휴지통에 있는 한 건을 완전히 지운다. 휴지통에 없는 건은 건드리지 않는다.</summary>
    Task DeleteForeverAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>휴지통을 비운다. 지운 건수를 돌려준다.</summary>
    Task<int> EmptyTrashAsync(CancellationToken cancellationToken);

    /// <summary>폴더를 옮긴다. 담겨 있지 않거나 휴지통에 있는 공고는 건드리지 않는다.</summary>
    Task MoveToFolderAsync(int noticeId, FavoriteFolder folder, CancellationToken cancellationToken);

    /// <summary>메모를 저장한다. 담겨 있지 않은 공고면 담으면서 저장한다(휴지통에 있으면 꺼낸다). 길면 잘라 저장한다.</summary>
    Task SaveMemoAsync(int noticeId, string memo, CancellationToken cancellationToken);

    /// <summary>주어진 공고 중 관심 공고(휴지통 제외)인 것의 번호.</summary>
    Task<IReadOnlySet<int>> FavoriteIdsAsync(IReadOnlyCollection<int> noticeIds, CancellationToken cancellationToken);

    Task<string?> MemoAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>진행 중(마감 전·취소 아님)은 마감 임박순, 마감·취소는 최근 마감순, 휴지통은 최근에 버린 순.</summary>
    Task<FavoriteList> ListAsync(DateTime now, CancellationToken cancellationToken);

    Task<int> CountOpenAsync(DateTime now, CancellationToken cancellationToken);
}
