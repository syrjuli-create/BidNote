namespace BidNote.Domain.Searching;

/// <summary>이름을 붙여 저장한 메인 화면 검색 조건. Query는 정리된 주소 쿼리(물음표 없이)다.</summary>
public class SavedSearch
{
    public const int MaxNameLength = 40;
    public const int MaxQueryLength = 2000;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>메인 화면에서 마지막으로 쓴 검색 조건. 한 줄(Id=1)만 둔다.</summary>
public class LastSearch
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string Query { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
