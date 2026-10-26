namespace BidNote.Domain.Bidding;

/// <summary>공사 묶음. 전기·정보통신·소방시설·국가유산 공사는 금액 구간이 따로 있다.</summary>
public enum ScreeningTrade
{
    General,
    Special,
}

/// <summary>
/// 적격심사 금액 구간 한 줄. <see cref="ExperiencePoints"/>가 0이면 시공경험을 평가하지 않는다.
/// 추정가격이 <see cref="From"/> 이상 <see cref="To"/> 미만인 공사에 쓴다.
/// </summary>
public sealed record ScreeningBand(
    ScreeningTrade Trade, long From, long To, string Name,
    int ExperiencePoints, double Multiple, double? CivilMultiple, int ManagementPoints, int PricePoints, double LowerLimitRate)
{
    public bool EvaluatesExperience => ExperiencePoints > 0;

    /// <summary>토목공사업으로 참여하면 배수가 다른 구간이 있다 (100억 미만~50억 이상: 2배, 토목 1배).</summary>
    public double MultipleFor(bool civil) => civil && CivilMultiple is { } c ? c : Multiple;
}

/// <param name="Points">예상 시공경험 점수 (배점 × min(1, 실적 ÷ 기준 실적)).</param>
/// <param name="RequiredPerformance">만점에 필요한 실적 = 기초금액 × 배수.</param>
public sealed record ExperienceEstimate(ScreeningBand Band, double Multiple, long RequiredPerformance, long Performance, double Points)
{
    public bool Full => Performance >= RequiredPerformance;
}

/// <summary>
/// 조달청 시설공사 적격심사 세부기준의 금액 구간별 배점·실적 배수·낙찰하한율 (참고용).
/// 출처: 조달청지침 제6481호, 2026.6.26 일부개정, 2026.7.1 시행 (모두입찰 정리표로 확인).
/// 낙찰하한율은 2026-10 수집한 공고 1만여 건의 sucsfbidLwltRate와 구간마다 일치했다.
/// 지방자치단체(행정안전부 낙찰자 결정기준)는 구간이 달라 아직 넣지 않았다. 실제 입찰은 공고문 기준을 따른다.
/// </summary>
public static class ScreeningRules
{
    public const string Source = "조달청 시설공사 적격심사 세부기준 (조달청지침 제6481호, 2026.7.1 시행)";

    /// <summary>시공경험 실적 기간(년).</summary>
    public const int PerformanceYears = 5;

    private const long Eok = 100_000_000;

    public static IReadOnlyList<ScreeningBand> Bands { get; } =
    [
        new(ScreeningTrade.General, 50 * Eok, 100 * Eok, "100억 미만 ~ 50억 이상", 15, 2, 1, 15, 50, 87.495),
        new(ScreeningTrade.General, 10 * Eok, 50 * Eok, "50억 미만 ~ 10억 이상", 15, 1, null, 15, 70, 88.745),
        new(ScreeningTrade.General, 3 * Eok, 10 * Eok, "10억 미만 ~ 3억 이상", 10, 0.5, null, 10, 80, 89.745),
        new(ScreeningTrade.General, 2 * Eok, 3 * Eok, "3억 미만 ~ 2억 이상", 5, 0.5, null, 5, 90, 89.745),
        new(ScreeningTrade.General, 0, 2 * Eok, "2억 미만", 0, 0, null, 10, 90, 89.745),
        new(ScreeningTrade.Special, 10 * Eok, 50 * Eok, "50억 미만 ~ 10억 이상", 15, 1, null, 15, 70, 88.745),
        new(ScreeningTrade.Special, 3 * Eok, 10 * Eok, "10억 미만 ~ 3억 이상", 15, 0.5, null, 15, 70, 89.745),
        new(ScreeningTrade.Special, 80_000_000, 3 * Eok, "3억 미만 ~ 8천만 원 이상", 5, 0.5, null, 5, 90, 89.745),
        new(ScreeningTrade.Special, 0, 80_000_000, "8천만 원 미만", 0, 0, null, 10, 90, 89.745),
    ];

    /// <summary>면허 이름으로 공사 묶음을 정한다. 전기·정보통신·소방시설·국가유산(문화재) 면허만 있으면 Special.</summary>
    public static ScreeningTrade TradeOf(IEnumerable<string> licenseNames)
    {
        ArgumentNullException.ThrowIfNull(licenseNames);
        var names = licenseNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        return names.Count > 0 && names.All(IsSpecial) ? ScreeningTrade.Special : ScreeningTrade.General;
    }

    private static bool IsSpecial(string name) =>
        name.Contains("전기공사", StringComparison.Ordinal) || name.Contains("정보통신공사", StringComparison.Ordinal)
        || name.Contains("소방시설", StringComparison.Ordinal) || name.Contains("국가유산", StringComparison.Ordinal)
        || name.Contains("문화재", StringComparison.Ordinal);

    /// <summary>추정가격이 들어가는 구간. 100억 이상(종합심사낙찰제 등)이거나 추정가격을 모르면 null.</summary>
    public static ScreeningBand? BandFor(long? estimatedPrice, ScreeningTrade trade)
    {
        if (estimatedPrice is not { } price || price < 0) return null;
        return Bands.FirstOrDefault(b => b.Trade == trade && price >= b.From && price < b.To);
    }

    /// <summary>
    /// 예상 시공경험 점수. 기준 실적 = 기초금액(없으면 추정가격) × 배수, 점수 = 배점 × min(1, 내 실적 ÷ 기준 실적).
    /// 시공경험을 평가하지 않는 구간이거나 금액·실적을 모르면 null.
    /// </summary>
    public static ExperienceEstimate? EstimateExperience(ScreeningBand? band, long? basePrice, long? estimatedPrice, long? performance, bool civil)
    {
        if (band is not { EvaluatesExperience: true } || performance is not { } mine || mine < 0) return null;
        long? basis = basePrice is > 0 ? basePrice : estimatedPrice is > 0 ? estimatedPrice : null;
        if (basis is not { } amount) return null;
        double multiple = band.MultipleFor(civil);
        long required = (long)Math.Ceiling(amount * multiple);
        double points = required <= 0 ? band.ExperiencePoints : band.ExperiencePoints * Math.Min(1, (double)mine / required);
        return new ExperienceEstimate(band, multiple, required, mine, Math.Round(points, 2));
    }
}
