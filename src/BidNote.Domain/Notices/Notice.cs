namespace BidNote.Domain.Notices;

/// <summary>공고의 현재 상태. 바뀌기 전 내용은 <see cref="NoticeHistory"/>에 남긴다.</summary>
public class Notice
{
    public int Id { get; set; }

    /// <summary>데이터 출처 (G2B, D2B, KEPCO, LH).</summary>
    public string SourceSystem { get; set; } = SourceSystems.G2B;

    public BusinessType BusinessType { get; set; }

    public string BidNo { get; set; } = "";

    /// <summary>공고 차수. 정정 공고가 나오면 올라간다.</summary>
    public string BidSeq { get; set; } = "00";

    public string Name { get; set; } = "";
    public string Agency { get; set; } = "";
    public string DemandAgency { get; set; } = "";
    /// <summary>시도 이름 (필터용). 예: 대전광역시</summary>
    public string Region { get; set; } = "";

    /// <summary>원문의 현장 지역 전체 이름. 예: 대전광역시 서구</summary>
    public string SiteRegionName { get; set; } = "";

    /// <summary>계약방법 묶음 (일반경쟁·제한경쟁·지명경쟁·수의계약). "제한경쟁(1등급)"의 등급은 떼고 저장한다.</summary>
    public string ContractMethod { get; set; } = "";

    /// <summary>대표 업종 (목록 표시용). 면허 제한 첫 줄이나 주공종.</summary>
    public string License { get; set; } = "";

    /// <summary>기초금액(원). 공고 뒤에 공개되기도 해서 비어 있을 수 있다.</summary>
    public long? BasePrice { get; set; }

    /// <summary>낙찰하한율(%).</summary>
    public double LowerLimitRate { get; set; }

    public long? AValue { get; set; }
    public long? NetCost { get; set; }

    /// <summary>기초금액이 공개된 일시.</summary>
    public DateTime? BasePriceOpenedAt { get; set; }

    /// <summary>예비가격 범위. 예: "-3% ~ +3%"</summary>
    public string ReservePriceRange { get; set; } = "";

    /// <summary>마지막으로 받은 기초금액 원문의 Hash. 같으면 다시 저장하지 않는다.</summary>
    public string BasePriceHash { get; set; } = "";

    public DateTime PostedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime OpenAt { get; set; }

    public bool IsRebid { get; set; }
    public int CorrectionCount { get; set; }

    public DateTime LastModifiedAt { get; set; }
    public DateTime FirstCollectedAt { get; set; }
    public DateTime LastCheckedAt { get; set; }

    /// <summary>마지막으로 받은 원본 응답의 Hash. 바뀌면 이력을 남긴다.</summary>
    public string RawHash { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public List<NoticeHistory> Histories { get; set; } = [];

    public List<NoticeLicense> Licenses { get; set; } = [];

    /// <summary>나라장터 공고 원문 링크.</summary>
    public string DetailUrl { get; set; } = "";
}
