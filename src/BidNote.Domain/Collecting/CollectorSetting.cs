namespace BidNote.Domain.Collecting;

/// <summary>수집 설정 (한 줄만 쓴다). 화면에서 바꾸면 다음 수집부터 적용된다.</summary>
public class CollectorSetting
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>처음 켤 때(이어 받을 기록이 없을 때) 거슬러 올라가 받을 일수.</summary>
    public int BackfillDays { get; set; }

    /// <summary>수집 주기(분).</summary>
    public int IntervalMinutes { get; set; }

    /// <summary>마지막으로 수집에 성공한 구간의 끝. 다음에 켜면 여기서부터 이어 받는다.</summary>
    public DateTime? LastSuccessTo { get; set; }

    /// <summary>개찰 결과 수집이 마지막으로 성공한 구간의 끝. 실패하면 다음 수집 때 여기서부터 다시 받는다.</summary>
    public DateTime? ResultsLastSuccessTo { get; set; }

    /// <summary>참가 가능 지역 수집이 마지막으로 성공한 구간의 끝.</summary>
    public DateTime? RegionsLastSuccessTo { get; set; }

    /// <summary>참가 가능 지역을 이 시각부터는 받아 두었다. 진행 공고가 이보다 앞서 올라왔으면 그 사이를 채운다.</summary>
    public DateTime? RegionsCoveredFrom { get; set; }

    public DateTime UpdatedAt { get; set; }
}
