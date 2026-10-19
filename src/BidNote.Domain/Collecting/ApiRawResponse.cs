using BidNote.Domain.Notices;

namespace BidNote.Domain.Collecting;

/// <summary>API에서 받은 원문. 파싱 오류나 필드 변경 때 다시 처리하는 데 쓴다.</summary>
public class ApiRawResponse
{
    public long Id { get; set; }
    public string SourceSystem { get; set; } = SourceSystems.G2B;
    public string ApiName { get; set; } = "";
    public string RequestParameter { get; set; } = "";
    public BusinessType BusinessType { get; set; }
    public string BidNo { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public int HttpStatus { get; set; }
    public DateTime CollectedAt { get; set; }
    public DateTime? ParsedAt { get; set; }
    public int ParseVersion { get; set; }
}
