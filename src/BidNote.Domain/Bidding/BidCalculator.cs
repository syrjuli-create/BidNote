namespace BidNote.Domain.Bidding;

/// <param name="BasePrice">기초금액(원)</param>
/// <param name="AssessmentRate">사정률(%)</param>
/// <param name="LowerLimitRate">낙찰하한율(%)</param>
/// <param name="AValue">A값(원). 없으면 0</param>
/// <param name="NetCost">순공사원가(원). 없으면 0</param>
public sealed record BidInput(long BasePrice, double AssessmentRate, double LowerLimitRate, long AValue = 0, long NetCost = 0);

public sealed record BidCalculation(
    decimal PlannedPrice,
    decimal BasicAmount,
    decimal? AValueAmount,
    decimal? NetCostFloor,
    long BidAmount,
    bool UsedNetCostFloor);

/// <summary>투찰금액 계산. 결과는 참고용이며 최종 금액은 공고문 기준을 따른다.</summary>
public static class BidCalculator
{
    /// <summary>순공사원가 하한에 곱하는 비율.</summary>
    public const decimal NetCostFloorRatio = 0.98m;

    public static BidCalculation Calculate(BidInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.BasePrice <= 0) throw new ArgumentOutOfRangeException(nameof(input), "기초금액은 0보다 커야 합니다.");
        if (input.AssessmentRate <= 0) throw new ArgumentOutOfRangeException(nameof(input), "사정률은 0보다 커야 합니다.");
        if (input.LowerLimitRate <= 0) throw new ArgumentOutOfRangeException(nameof(input), "낙찰하한율은 0보다 커야 합니다.");

        decimal rate = (decimal)input.AssessmentRate / 100m;
        decimal lower = (decimal)input.LowerLimitRate / 100m;

        decimal planned = input.BasePrice * rate;
        decimal basic = planned * lower;
        decimal? withA = input.AValue > 0 ? (planned - input.AValue) * lower + input.AValue : null;
        decimal? floor = input.NetCost > 0 ? input.NetCost * rate * NetCostFloorRatio : null;

        decimal main = withA ?? basic;
        bool usedFloor = floor is { } f && f > main;
        long bid = (long)Math.Ceiling(usedFloor ? floor!.Value : main);

        return new BidCalculation(planned, basic, withA, floor, bid, usedFloor);
    }

    /// <summary>투찰금액에 해당하는 사정률(%)을 거꾸로 계산한다.</summary>
    public static double AssessmentRateFor(long basePrice, double lowerLimitRate, long aValue, long bidAmount)
    {
        if (basePrice <= 0) throw new ArgumentOutOfRangeException(nameof(basePrice));
        if (lowerLimitRate <= 0) throw new ArgumentOutOfRangeException(nameof(lowerLimitRate));

        decimal lower = (decimal)lowerLimitRate / 100m;
        decimal planned = aValue > 0 ? (bidAmount - aValue) / lower + aValue : bidAmount / lower;
        return (double)(planned / basePrice * 100m);
    }
}
