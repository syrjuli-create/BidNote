namespace BidNote.Domain.Collecting;

/// <summary>API별 하루 호출 수. 공공데이터포털의 하루 호출 한도를 넘지 않게 관리한다.</summary>
public class ApiUsage
{
    public int Id { get; set; }

    /// <summary>호출한 날짜 (PC 기준 현지 날짜).</summary>
    public DateOnly Date { get; set; }

    public string ApiName { get; set; } = "";
    public int Count { get; set; }
    public DateTime LastCalledAt { get; set; }
}
