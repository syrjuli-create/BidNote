namespace BidNote.Application.Favorites;

public sealed record FavoriteItem(
    int NoticeId, string BidNo, string Name, string Agency, string Region, string License,
    long? BasePrice, DateTime DueAt, bool IsActive, int CorrectionCount, string Memo, DateTime AddedAt);

public sealed record FavoriteList(IReadOnlyList<FavoriteItem> Open, IReadOnlyList<FavoriteItem> Closed);

public interface IFavoriteStore
{
    /// <summary>담겨 있으면 빼고, 없으면 담는다. 바뀐 뒤의 상태(담겼으면 true)를 돌려준다.</summary>
    Task<bool> ToggleAsync(int noticeId, CancellationToken cancellationToken);

    Task RemoveAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>메모를 저장한다. 담겨 있지 않은 공고면 담으면서 저장한다. 길면 잘라 저장한다.</summary>
    Task SaveMemoAsync(int noticeId, string memo, CancellationToken cancellationToken);

    /// <summary>주어진 공고 중 관심 공고인 것의 번호.</summary>
    Task<IReadOnlySet<int>> FavoriteIdsAsync(IReadOnlyCollection<int> noticeIds, CancellationToken cancellationToken);

    Task<string?> MemoAsync(int noticeId, CancellationToken cancellationToken);

    /// <summary>진행 중(마감 전·취소 아님)은 마감 임박순, 나머지는 최근 마감순.</summary>
    Task<FavoriteList> ListAsync(DateTime now, CancellationToken cancellationToken);

    Task<int> CountOpenAsync(DateTime now, CancellationToken cancellationToken);
}
