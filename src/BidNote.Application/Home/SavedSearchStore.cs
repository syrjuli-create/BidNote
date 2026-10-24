namespace BidNote.Application.Home;

public sealed record SavedSearchItem(int Id, string Name, string Query, DateTime UpdatedAt);

/// <summary>메인 화면 검색 조건 저장. 이름 붙인 조건 목록과, 마지막으로 쓴 조건 하나를 둔다.</summary>
public interface ISavedSearchStore
{
    /// <summary>이름순.</summary>
    Task<IReadOnlyList<SavedSearchItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 조건을 이름 붙여 저장한다. 같은 이름이 있으면 조건을 덮어쓴다. 이름은 앞뒤 공백을 떼고 길면 자른다.
    /// 이름이나 조건(정리한 뒤)이 비어 있으면 ArgumentException.
    /// </summary>
    Task<SavedSearchItem> SaveAsync(string name, string query, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>마지막으로 쓴 조건. 없으면 빈 문자열.</summary>
    Task<string> GetLastAsync(CancellationToken cancellationToken);

    /// <summary>마지막 조건을 바꾼다. 정리한 뒤 비어 있으면 지운다.</summary>
    Task SetLastAsync(string query, CancellationToken cancellationToken);
}
