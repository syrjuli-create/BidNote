namespace BidNote.Domain.Bidding;

/// <summary>등급표 한 칸. <see cref="ShortRatio"/>는 실적인정기간이 4년 미만인 업체의 기준(없으면 같다).</summary>
public sealed record LocalGrade(string Grade, string Ratio, double Points, string ShortRatio = "");

/// <summary>등급표 하나 (예: 종합공사 업종등급점수).</summary>
public sealed record LocalGradeTable(string Title, IReadOnlyList<LocalGrade> Grades)
{
    public bool HasShortPeriod => Grades.Any(g => g.ShortRatio.Length > 0);
}

/// <summary>
/// 지방자치단체 시설공사 적격심사 금액 구간 한 줄 (시공실적으로 참가자격을 제한하지 않은 입찰의 시공경험 기준).
/// 실적은 기초금액이 아니라 업종별 <b>추정가격</b> 대비 비율로 보고, 대부분 등급 계단식으로 점수를 준다.
/// </summary>
public sealed record LocalScreeningBand(
    long From, long To, string Name, string Appendix,
    int CapabilityPoints, string ExperiencePoints, string Period, string Method, int PricePoints,
    IReadOnlyList<LocalGradeTable> Grades);

/// <summary>
/// 행정안전부 「지방자치단체 입찰시 낙찰자 결정기준」 제2장의1 시설공사 적격심사 세부기준 (참고용).
/// 출처: 행정안전부 예규 제325호, 2025.4.30 일부개정, 2025.7.1 시행 — 원문 [별표 1]~[별표 7]에서 옮겼다.
/// 지자체는 업체의 실적인정기간(4년 이상/미만), 종합·전문 구분, 3년·5년 실적을 따로 봐야 해서 점수를 자동 계산하지 않고 표만 보여 준다.
/// </summary>
public static class LocalScreeningRules
{
    public const string Source = "행정안전부 지방자치단체 입찰시 낙찰자 결정기준 (예규 제325호, 2025.7.1 시행)";

    private const long Eok = 100_000_000;

    private static readonly LocalGradeTable GeneralEight = new("종합공사 업종등급점수",
    [
        new("A", "100% 이상", 15), new("B", "90% 이상", 13), new("C", "80% 이상", 11), new("D", "70% 이상", 9),
        new("E", "60% 이상", 7), new("F", "50% 이상", 5), new("G", "40% 이상", 3), new("H", "40% 미만", 1),
    ]);

    private static readonly LocalGradeTable SpecialtyEight = new("전문·그 밖의 공사 업종등급점수",
    [
        new("A", "80% 이상", 15), new("B", "70% 이상", 13), new("C", "60% 이상", 11), new("D", "50% 이상", 9),
        new("E", "40% 이상", 7), new("F", "30% 이상", 5), new("G", "20% 이상", 3), new("H", "20% 미만", 1),
    ]);

    public static IReadOnlyList<LocalScreeningBand> Bands { get; } =
    [
        new(100 * Eok, 300 * Eok, "300억 미만 ~ 100억 이상", "별표 1", 44, "14점", "최근 3년 (실적계수)",
            "실적계수로 평가 — 세부 산식은 원문 [별표 1] 참조", 30, []),
        new(50 * Eok, 100 * Eok, "100억 미만 ~ 50억 이상", "별표 2", 30, "15점", "최근 3년",
            "점수 = 15 × 실적계수 × 업종평가비율, 실적계수 = 최근 3년 업종별 실적 ÷ (업종별 추정가격 × 1.7), 상한 1 (등급별 입찰제한 공사는 × 1.2)", 50, []),
        new(30 * Eok, 50 * Eok, "50억 미만 ~ 30억 이상", "별표 3", 25, "15점", "최근 5년",
            "업종별 추정가격 대비 최근 5년 업종별 실적(관급·지급자재비 제외) 비율의 등급점수 × 업종평가비율", 70, [GeneralEight, SpecialtyEight]),
        new(10 * Eok, 30 * Eok, "30억 미만 ~ 10억 이상", "별표 4", 30, "15점", "최근 5년",
            "업종별 추정가격 대비 최근 5년 업종별 실적(관급·지급자재비 제외) 비율의 등급점수 × 업종평가비율", 70, [GeneralEight, SpecialtyEight]),
        new(4 * Eok, 10 * Eok, "10억 미만 ~ 4억 이상", "별표 5", 20, "10점", "최근 3년 + 다음 해 1월 1일 ~ 입찰공고일",
            "업종별 추정가격 대비 실적 비율의 등급점수 × 업종평가비율 (실적인정기간 4년 미만 업체는 기준이 낮다)", 80,
            [new("업종등급점수",
            [
                new("A", "70% 이상", 10, "60% 이상"), new("B", "60% 이상", 9, "50% 이상"), new("C", "50% 이상", 8, "40% 이상"),
                new("D", "40% 이상", 7, "30% 이상"), new("E", "30% 이상", 6, "20% 이상"), new("F", "20% 이상", 5, "10% 이상"),
                new("G", "20% 미만", 4, "10% 미만"),
            ])]),
        new(2 * Eok, 4 * Eok, "4억 미만 ~ 2억 이상", "별표 6", 10, "종합 4.8점 / 전문·그 밖 5점", "최근 3년 + 다음 해 1월 1일 ~ 입찰공고일",
            "업종별 추정가격 대비 실적 비율의 등급점수 × 업종평가비율 (실적인정기간 4년 미만 업체는 기준이 낮다)", 90,
            [
                new("종합공사 업종등급점수",
                [
                    new("A", "60% 이상", 4.8, "50% 이상"), new("B", "50% 이상", 3.8, "40% 이상"), new("C", "40% 이상", 2.8, "30% 이상"),
                    new("D", "30% 이상", 1.8, "20% 이상"), new("E", "30% 미만", 0.8, "20% 미만"),
                ]),
                new("전문·그 밖의 공사 업종등급점수",
                [
                    new("A", "60% 이상", 5, "50% 이상"), new("B", "50% 이상", 4, "40% 이상"), new("C", "40% 이상", 3, "30% 이상"),
                    new("D", "30% 이상", 2, "20% 이상"), new("E", "30% 미만", 1, "20% 미만"),
                ]),
            ]),
        new(0, 2 * Eok, "2억 미만", "별표 7", 10, "4점", "별표 6과 같음",
            "별표 6 시공경험 점수 × 4/4.8 (종합) 또는 × 4/5 (전문·그 밖)", 90, []),
    ];

    /// <summary>추정가격이 들어가는 구간. 300억 이상이거나 추정가격을 모르면 null.</summary>
    public static LocalScreeningBand? BandFor(long? estimatedPrice) =>
        estimatedPrice is { } price && price >= 0 ? Bands.FirstOrDefault(b => price >= b.From && price < b.To) : null;

    /// <summary>
    /// 발주기관이 지방자치단체(시·군·구·도, 특별시·광역시·특별자치시도, 교육청)인지 공고기관 이름으로 판단한다.
    /// 지자체는 행정안전부 기준, 그 밖(조달청·국가기관·공기업 등)은 조달청 기준으로 본다.
    /// </summary>
    public static bool IsLocalAgency(string? agency)
    {
        string name = (agency ?? "").Trim();
        if (name.Length == 0 || name.Contains("조달청", StringComparison.Ordinal)) return false;
        if (name.Contains("특별시", StringComparison.Ordinal) || name.Contains("광역시", StringComparison.Ordinal)
            || name.Contains("특별자치", StringComparison.Ordinal) || name.Contains("교육청", StringComparison.Ordinal)
            || name.Contains("교육지원청", StringComparison.Ordinal))
            return true;
        string first = name.Split(' ', 2)[0];
        return first.Length >= 2 && first[^1] is '시' or '군' or '구' or '도';
    }
}
