using BidNote.Domain.Bidding;
using BidNote.Domain.Notices;

namespace BidNote.Domain.Results;

/// <summary>
/// 공고 하나의 개찰 결과. 개찰 목록에서 받은 요약(1순위·참가 수)과,
/// 따로 받은 예정가격·업체별 순위(<see cref="Ranks"/>)를 둔다. 재입찰이면 가장 마지막 차례만 남긴다.
/// </summary>
public class BidResult
{
    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    /// <summary>결과를 받은 공고 차수·분류 번호·재입찰 번호. 순위·예비가격을 받을 때 같은 차례를 고른다.</summary>
    public string BidSeq { get; set; } = "";
    public string ClassNo { get; set; } = "";
    public string RebidNo { get; set; } = "";

    /// <summary>진행 구분 원문 그대로 (개찰완료, 유찰 등).</summary>
    public string Status { get; set; } = "";
    public int ParticipantCount { get; set; }
    public DateTime? OpenedAt { get; set; }

    public string WinnerName { get; set; } = "";
    public string WinnerBizNo { get; set; } = "";
    public long? WinnerAmount { get; set; }

    /// <summary>1순위 투찰률(%) = 투찰금액 / 예정가격.</summary>
    public double? WinnerBidRate { get; set; }

    /// <summary>예정가격. 예비가격 상세를 받으면 채운다.</summary>
    public long? PlannedPrice { get; set; }

    /// <summary>예비가격 상세에 같이 오는 기초금액. 공고의 기초금액과 다를 수 있어 따로 둔다.</summary>
    public long? ResultBasePrice { get; set; }

    /// <summary>업체별 순위를 받은 시각. null이면 아직 받지 않았다.</summary>
    public DateTime? RanksFetchedAt { get; set; }

    /// <summary>순위를 끝까지 받았는지 (호출 한도에 걸리면 일부만 받는다).</summary>
    public bool RanksComplete { get; set; }

    /// <summary>개찰 목록 원문의 Hash. 같으면 다시 저장하지 않는다.</summary>
    public string RawHash { get; set; } = "";
    public DateTime UpdatedAt { get; set; }

    public List<BidResultRank> Ranks { get; set; } = [];

    public bool IsOpened => Status.Contains("개찰완료", StringComparison.Ordinal);
}

/// <summary>개찰 결과의 업체 한 줄. 하한 미달·예가 초과·취소한 업체는 순위가 없다.</summary>
public class BidResultRank
{
    public const int MaxNameLength = 200;

    public int Id { get; set; }
    public int BidResultId { get; set; }

    public int? Rank { get; set; }
    public string CompanyName { get; set; } = "";
    public string BizNo { get; set; } = "";
    public long? Amount { get; set; }

    /// <summary>투찰률(%) = 투찰금액 / 예정가격.</summary>
    public double? BidRate { get; set; }

    /// <summary>비고 원문 (정상, 낙찰하한선 미달, 예가초과 …).</summary>
    public string Remark { get; set; } = "";
}

/// <summary>내 투찰금액이 개찰 결과에서 어디쯤인지.</summary>
public enum MyRankKind
{
    /// <summary>순위를 아직 받지 않아 모른다.</summary>
    Unknown,
    Ranked,
    BelowFloor,
    OverPlanned,

    /// <summary>내 사업자번호로 개찰 결과에서 우리 회사를 찾았다. 하한 미달 등으로 순위에서 빠졌으면 Rank가 null이고 Remark에 이유가 온다.</summary>
    Actual,
}

/// <summary>Ranked·BelowFloor·OverPlanned는 내 투찰금액으로 짐작한 것, Actual은 개찰 결과에 나온 실제 순위.</summary>
public sealed record MyRank(MyRankKind Kind, int? Rank, string Remark = "");

/// <summary>개찰 결과 계산. 사정률 역산은 투찰금액 계산기와 같은 산식을 쓴다.</summary>
public static class BidResultMath
{
    /// <summary>실제 사정률(%) = 예정가격 / 기초금액.</summary>
    public static double? ActualAssessmentRate(long? plannedPrice, long? basePrice) =>
        plannedPrice is > 0 && basePrice is > 0 ? (double)plannedPrice.Value / basePrice.Value * 100 : null;

    /// <summary>예정가격 기준 낙찰하한 금액 = (예정가격 − A값) × 낙찰하한율 + A값, 원 단위 올림.</summary>
    public static long? LowerLimitAmount(long? plannedPrice, double lowerLimitRate, long aValue)
    {
        if (plannedPrice is not > 0 || lowerLimitRate <= 0) return null;
        decimal lower = (decimal)lowerLimitRate / 100m;
        decimal a = aValue > 0 ? aValue : 0;
        return (long)Math.Ceiling((plannedPrice.Value - a) * lower + a);
    }

    /// <summary>투찰금액을 사정률(%)로 바꾼다. 기초금액이나 낙찰하한율을 모르면 null.</summary>
    public static double? AssessmentRateOf(long? amount, long? basePrice, double lowerLimitRate, long aValue) =>
        amount is > 0 && basePrice is > 0 && lowerLimitRate > 0
            ? BidCalculator.AssessmentRateFor(basePrice.Value, lowerLimitRate, aValue, amount.Value)
            : null;

    /// <summary>사업자번호 비교용: 숫자만 남긴다 ("123-45-67890" → "1234567890").</summary>
    public static string BizNoKey(string? bizNo) => new((bizNo ?? "").Where(char.IsAsciiDigit).ToArray());

    /// <summary>
    /// 내 투찰금액이 몇 순위였을지. 순위가 있는 업체 중 나보다 낮게 쓴 곳의 수 + 1로 본다.
    /// 낙찰하한 금액보다 낮으면 하한 미달, 예정가격보다 높으면 예가 초과.
    /// </summary>
    public static MyRank RankOf(long myAmount, IEnumerable<long> rankedAmounts, long? lowerLimitAmount, long? plannedPrice)
    {
        ArgumentNullException.ThrowIfNull(rankedAmounts);
        if (lowerLimitAmount is { } floor && myAmount < floor) return new MyRank(MyRankKind.BelowFloor, null);
        if (plannedPrice is { } planned && myAmount > planned) return new MyRank(MyRankKind.OverPlanned, null);
        return new MyRank(MyRankKind.Ranked, rankedAmounts.Count(x => x < myAmount) + 1);
    }
}
