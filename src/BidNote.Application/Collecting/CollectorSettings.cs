namespace BidNote.Application.Collecting;

public sealed record CollectorSettingsValues(int BackfillDays, int IntervalMinutes, DateTime? LastSuccessTo);

/// <summary>설정 파일의 기본값. 화면에서 한 번이라도 저장하면 DB 값이 우선한다.</summary>
public sealed class CollectorOptions
{
    public const string SectionName = "Collector";
    public int BackfillDays { get; set; } = 7;
    public int IntervalMinutes { get; set; } = 10;
}

public interface ICollectorSettingsStore
{
    Task<CollectorSettingsValues> GetAsync(CancellationToken cancellationToken);

    /// <summary>범위를 벗어난 값이면 <see cref="ArgumentOutOfRangeException"/>.</summary>
    Task SaveAsync(int backfillDays, int intervalMinutes, CancellationToken cancellationToken);

    Task MarkSuccessAsync(DateTime collectedTo, CancellationToken cancellationToken);
}

public static class CollectorRules
{
    public const int MinBackfillDays = 1, MaxBackfillDays = 60;
    public const int MinIntervalMinutes = 5, MaxIntervalMinutes = 1440;

    public static readonly int[] BackfillChoices = [7, 14, 30, 60];
    public static readonly int[] IntervalChoices = [10, 30, 60, 180];

    // 2026-10-19 실측 7일치 기준 하루 평균 건수 (공사 공고)
    private const double NoticesPerDay = 405, LicensesPerDay = 1427, BasePricesPerDay = 305;

    public static void Validate(int backfillDays, int intervalMinutes)
    {
        if (backfillDays is < MinBackfillDays or > MaxBackfillDays)
            throw new ArgumentOutOfRangeException(nameof(backfillDays), $"받을 기간은 {MinBackfillDays}~{MaxBackfillDays}일이어야 합니다.");
        if (intervalMinutes is < MinIntervalMinutes or > MaxIntervalMinutes)
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes), $"수집 주기는 {MinIntervalMinutes}~{MaxIntervalMinutes}분이어야 합니다.");
    }

    /// <summary>
    /// 이번에 켰을 때 어디서부터 받을지. 마지막 성공 시각이 있으면 거기서 이어 받되,
    /// 너무 오래됐으면 최대 기간까지만 거슬러 올라간다. 기록이 없으면 설정한 기간만큼 받는다.
    /// </summary>
    public static DateTime StartFrom(DateTime now, DateTime? lastSuccessTo, int backfillDays)
    {
        DateTime oldest = now.AddDays(-MaxBackfillDays);
        if (lastSuccessTo is { } last) return last < oldest ? oldest : last;
        return now.AddDays(-backfillDays);
    }

    /// <summary>며칠치를 받을 때의 대략적인 API 호출 수 (공고 목록·면허·기초금액, 한 번에 999건).</summary>
    public static int EstimateCalls(double days, int pageSize = 999) =>
        Pages(days * NoticesPerDay, pageSize) + Pages(days * LicensesPerDay, pageSize) + Pages(days * BasePricesPerDay, pageSize);

    private static int Pages(double rows, int pageSize) => Math.Max(1, (int)Math.Ceiling(rows / pageSize));
}
