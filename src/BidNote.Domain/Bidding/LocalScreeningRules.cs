namespace BidNote.Domain.Bidding;

/// <summary>
/// 등급표 한 칸. 실적 비율이 <see cref="Min"/>% 이상이면 이 등급이다 (마지막 칸은 0 = 그 아래 전부).
/// <see cref="ShortMin"/>은 실적인정기간이 4년 미만인 업체의 기준이다 (없으면 같은 기준).
/// </summary>
public sealed record LocalGrade(string Grade, double Min, double Points, double? ShortMin = null);

/// <summary>등급표 하나 (예: 종합공사 업종등급점수). 높은 등급부터 차례로 둔다.</summary>
public sealed record LocalGradeTable(string Title, IReadOnlyList<LocalGrade> Grades)
{
    public bool HasShortPeriod => Grades.Any(g => g.ShortMin is not null);

    /// <summary>칸의 비율 글자: "70% 이상", 마지막 칸은 바로 위 칸 기준으로 "20% 미만".</summary>
    public string RatioText(int index, bool shortPeriod = false)
    {
        double Min(int i) => shortPeriod && Grades[i].ShortMin is { } s ? s : Grades[i].Min;
        return index == Grades.Count - 1 && index > 0 ? $"{Min(index - 1):0.#}% 미만" : $"{Min(index):0.#}% 이상";
    }

    /// <summary>실적 비율(%)이 들어가는 칸.</summary>
    public int IndexFor(double ratioPercent, bool shortPeriod = false)
    {
        for (int i = 0; i < Grades.Count - 1; i++)
        {
            double min = shortPeriod && Grades[i].ShortMin is { } s ? s : Grades[i].Min;
            if (ratioPercent >= min) return i;
        }
        return Grades.Count - 1;
    }
}

/// <summary>지자체 시공경험을 어떻게 매기는지 (실적으로 참가자격을 제한하지 않은 입찰).</summary>
public enum LocalExperienceMethod
{
    /// <summary>원문 산식이 복잡해 계산하지 않는다 (300억~100억).</summary>
    NotEstimated,

    /// <summary>점수 = 배점 × min(1, 최근 3년 실적 ÷ (추정가격 × 계수)).</summary>
    Coefficient,

    /// <summary>최근 5년 실적 ÷ 추정가격 비율의 등급점수.</summary>
    Grade5Years,

    /// <summary>최근 3년(+다음 해 1월 1일~공고일) 실적 ÷ 추정가격 비율의 등급점수.</summary>
    Grade3Years,

    /// <summary>2억~4억 구간 점수에 비율을 곱한다 (2억 미만).</summary>
    ScaledFromBelow4Eok,
}

/// <summary>
/// 지방자치단체 시설공사 적격심사 금액 구간 한 줄 (시공실적으로 참가자격을 제한하지 않은 입찰의 시공경험 기준).
/// 실적은 기초금액이 아니라 업종별 <b>추정가격</b> 대비 비율로 보고, 대부분 등급 계단식으로 점수를 준다.
/// </summary>
public sealed record LocalScreeningBand(
    long From, long To, string Name, string Appendix,
    int CapabilityPoints, string ExperiencePoints, string Period, string Method, int PricePoints,
    IReadOnlyList<LocalGradeTable> Grades, LocalExperienceMethod Kind, double Coefficient = 0)
{
    /// <summary>종합공사 등급표 (등급표가 하나면 그것).</summary>
    public LocalGradeTable? TableFor(bool general) =>
        Grades.Count switch { 0 => null, 1 => Grades[0], _ => general ? Grades[0] : Grades[1] };
}

/// <param name="Grade">등급 (계수 방식이면 빈 값).</param>
/// <param name="RatioPercent">내 실적 ÷ 추정가격 (%).</param>
/// <param name="PeriodLabel">계산에 쓴 실적 ("최근 5년" 등).</param>
public sealed record LocalExperienceEstimate(double Points, double MaxPoints, string Grade, double RatioPercent, string PeriodLabel, string TableTitle);

/// <summary>
/// 행정안전부 「지방자치단체 입찰시 낙찰자 결정기준」 제2장의1 시설공사 적격심사 세부기준 (참고용).
/// 출처: 행정안전부 예규 제325호, 2025.4.30 일부개정, 2025.7.1 시행 — 원문 [별표 1]~[별표 7]에서 옮겼다.
/// </summary>
public static class LocalScreeningRules
{
    public const string Source = "행정안전부 지방자치단체 입찰시 낙찰자 결정기준 (예규 제325호, 2025.7.1 시행)";

    private const long Eok = 100_000_000;

    private static readonly LocalGradeTable GeneralEight = new("종합공사 업종등급점수",
    [
        new("A", 100, 15), new("B", 90, 13), new("C", 80, 11), new("D", 70, 9),
        new("E", 60, 7), new("F", 50, 5), new("G", 40, 3), new("H", 0, 1),
    ]);

    private static readonly LocalGradeTable SpecialtyEight = new("전문·그 밖의 공사 업종등급점수",
    [
        new("A", 80, 15), new("B", 70, 13), new("C", 60, 11), new("D", 50, 9),
        new("E", 40, 7), new("F", 30, 5), new("G", 20, 3), new("H", 0, 1),
    ]);

    private static readonly LocalGradeTable Below4General = new("종합공사 업종등급점수",
    [
        new("A", 60, 4.8, 50), new("B", 50, 3.8, 40), new("C", 40, 2.8, 30), new("D", 30, 1.8, 20), new("E", 0, 0.8, 0),
    ]);

    private static readonly LocalGradeTable Below4Specialty = new("전문·그 밖의 공사 업종등급점수",
    [
        new("A", 60, 5, 50), new("B", 50, 4, 40), new("C", 40, 3, 30), new("D", 30, 2, 20), new("E", 0, 1, 0),
    ]);

    private const string ThreeYears = "최근 3년 + 다음 해 1월 1일 ~ 입찰공고일";

    public static IReadOnlyList<LocalScreeningBand> Bands { get; } =
    [
        new(100 * Eok, 300 * Eok, "300억 미만 ~ 100억 이상", "별표 1", 44, "14점", "최근 3년 (실적계수)",
            "실적계수로 평가 — 세부 산식은 원문 [별표 1] 참조", 30, [], LocalExperienceMethod.NotEstimated),
        new(50 * Eok, 100 * Eok, "100억 미만 ~ 50억 이상", "별표 2", 30, "15점", "최근 3년",
            "점수 = 15 × 실적계수 × 업종평가비율, 실적계수 = 최근 3년 업종별 실적 ÷ (업종별 추정가격 × 1.7), 상한 1 (등급별 입찰제한 공사는 × 1.2)", 50, [],
            LocalExperienceMethod.Coefficient, 1.7),
        new(30 * Eok, 50 * Eok, "50억 미만 ~ 30억 이상", "별표 3", 25, "15점", "최근 5년",
            "업종별 추정가격 대비 최근 5년 업종별 실적(관급·지급자재비 제외) 비율의 등급점수 × 업종평가비율", 70, [GeneralEight, SpecialtyEight],
            LocalExperienceMethod.Grade5Years),
        new(10 * Eok, 30 * Eok, "30억 미만 ~ 10억 이상", "별표 4", 30, "15점", "최근 5년",
            "업종별 추정가격 대비 최근 5년 업종별 실적(관급·지급자재비 제외) 비율의 등급점수 × 업종평가비율", 70, [GeneralEight, SpecialtyEight],
            LocalExperienceMethod.Grade5Years),
        new(4 * Eok, 10 * Eok, "10억 미만 ~ 4억 이상", "별표 5", 20, "10점", ThreeYears,
            "업종별 추정가격 대비 실적 비율의 등급점수 × 업종평가비율 (실적인정기간 4년 미만 업체는 기준이 낮다)", 80,
            [new("업종등급점수",
            [
                new("A", 70, 10, 60), new("B", 60, 9, 50), new("C", 50, 8, 40), new("D", 40, 7, 30),
                new("E", 30, 6, 20), new("F", 20, 5, 10), new("G", 0, 4, 0),
            ])],
            LocalExperienceMethod.Grade3Years),
        new(2 * Eok, 4 * Eok, "4억 미만 ~ 2억 이상", "별표 6", 10, "종합 4.8점 / 전문·그 밖 5점", ThreeYears,
            "업종별 추정가격 대비 실적 비율의 등급점수 × 업종평가비율 (실적인정기간 4년 미만 업체는 기준이 낮다)", 90,
            [Below4General, Below4Specialty], LocalExperienceMethod.Grade3Years),
        new(0, 2 * Eok, "2억 미만", "별표 7", 10, "4점", ThreeYears,
            "별표 6 시공경험 점수 × 4/4.8 (종합) 또는 × 4/5 (전문·그 밖)", 90, [], LocalExperienceMethod.ScaledFromBelow4Eok),
    ];

    /// <summary>추정가격이 들어가는 구간. 300억 이상이거나 추정가격을 모르면 null.</summary>
    public static LocalScreeningBand? BandFor(long? estimatedPrice) =>
        estimatedPrice is { } price && price >= 0 ? Bands.FirstOrDefault(b => price >= b.From && price < b.To) : null;

    /// <summary>
    /// 건설산업기본법의 종합공사업인지 (토목·건축·토목건축·산업환경설비·조경). 아니면 전문·그 밖의 공사로 본다.
    /// </summary>
    public static bool IsGeneralLicense(string? name)
    {
        string n = (name ?? "").Replace("ㆍ", "", StringComparison.Ordinal).Replace("·", "", StringComparison.Ordinal).Trim();
        return n is "토목공사업" or "건축공사업" or "토목건축공사업" or "산업환경설비공사업" or "조경공사업";
    }

    /// <summary>
    /// 지자체 시공경험 예상 점수 (실적제한이 아닌 입찰, 업종 하나·업종평가비율 100% 가정). 필요한 실적을 모르거나
    /// 계산하지 않는 구간이면 null. <paramref name="performance5Years"/>는 최근 5년, <paramref name="performance3Years"/>는
    /// 최근 3년(+다음 해 1월 1일~공고일) 실적.
    /// </summary>
    public static LocalExperienceEstimate? EstimateExperience(LocalScreeningBand? band, long? estimatedPrice, bool general,
        long? performance5Years, long? performance3Years, bool underFourYears)
    {
        if (band is null || estimatedPrice is not > 0) return null;
        long price = estimatedPrice.Value;
        switch (band.Kind)
        {
            case LocalExperienceMethod.Coefficient when performance3Years is { } p3:
                {
                    double ratio = p3 * 100.0 / price;
                    double points = 15 * Math.Min(1, p3 / (price * band.Coefficient));
                    return new LocalExperienceEstimate(Math.Round(points, 2), 15, "", ratio, "최근 3년", $"실적계수 (추정가격 × {band.Coefficient:0.#})");
                }
            case LocalExperienceMethod.Grade5Years when performance5Years is { } p5:
                return ByGrade(band.TableFor(general)!, p5 * 100.0 / price, false, "최근 5년", 1);
            case LocalExperienceMethod.Grade3Years when performance3Years is { } p3:
                return ByGrade(band.TableFor(general)!, p3 * 100.0 / price, underFourYears, "최근 3년", 1);
            case LocalExperienceMethod.ScaledFromBelow4Eok when performance3Years is { } p3:
                {
                    var below4 = Bands.First(b => b.Appendix == "별표 6");
                    double max = general ? 4.8 : 5.0;
                    return ByGrade(below4.TableFor(general)!, p3 * 100.0 / price, underFourYears, "최근 3년", 4 / max);
                }
            default:
                return null;
        }
    }

    private static LocalExperienceEstimate ByGrade(LocalGradeTable table, double ratio, bool shortPeriod, string period, double scale)
    {
        var grade = table.Grades[table.IndexFor(ratio, shortPeriod)];
        return new LocalExperienceEstimate(Math.Round(grade.Points * scale, 2), Math.Round(table.Grades[0].Points * scale, 2),
            grade.Grade, ratio, period + (shortPeriod ? " · 실적인정기간 4년 미만 기준" : ""), table.Title);
    }

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
