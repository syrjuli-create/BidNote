namespace BidNote.Application.Results;

/// <summary>
/// 업체 하나의 투찰 성향 요약. 사정률은 투찰금액을 기초금액·A값·낙찰하한율로 거꾸로 계산한 값(%)이다.
/// LowRate~HighRate는 가운데 절반(25~75%)이 써낸 범위, MedianRate는 가운데 값.
/// </summary>
public sealed record CompetitorSummary(
    string BizNo, string Name, int Meetings, int Wins,
    double? MedianRate, double? LowRate, double? HighRate, DateTime? LastMetAt);

/// <param name="ActualRate">그 공고의 실제 사정률 (예정가격 ÷ 기초금액).</param>
/// <param name="MyRate">내가 그 공고에 기록한 사정률 (없으면 null).</param>
public sealed record CompetitorBid(
    int NoticeId, string NoticeName, DateTime? OpenedAt, int ParticipantCount,
    int? Rank, long? Amount, double? AssessmentRate, double? ActualRate, string Remark, double? MyRate);

/// <summary>사정률 분포 막대 하나: [From, To) 구간에 써낸 횟수.</summary>
public sealed record RateBin(double From, double To, int Count);

public sealed record CompetitorDetail(CompetitorSummary Summary, IReadOnlyList<CompetitorBid> Bids, IReadOnlyList<RateBin> Histogram);

/// <param name="NoticeCount">업체별 순위를 받아 둔 공고 수 (성향을 계산한 바탕).</param>
public sealed record CompetitorOverview(int NoticeCount, int CompanyCount, IReadOnlyList<CompetitorSummary> Companies);

public interface ICompetitorQueries
{
    /// <summary>자주 만난 업체순. <paramref name="search"/>는 업체명 일부. 최소 <paramref name="minMeetings"/>번 만난 업체만.</summary>
    Task<CompetitorOverview> OverviewAsync(string? search, int minMeetings, int top, CancellationToken cancellationToken);

    /// <summary>업체 하나(사업자번호)의 공고별 투찰과 사정률 분포. 없으면 null.</summary>
    Task<CompetitorDetail?> GetAsync(string bizNo, CancellationToken cancellationToken);
}

/// <summary>성향 계산 (가운데 값·사분위·분포 막대).</summary>
public static class CompetitorMath
{
    /// <summary>분포 막대 최소 개수 (0.1%p 간격이면 1%p 폭).</summary>
    public const int MinBins = 10;

    /// <summary>정렬한 값에서 p(0~1) 위치의 값 (선형 보간). 비어 있으면 null.</summary>
    public static double? Quantile(IReadOnlyList<double> sorted, double p)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count == 0) return null;
        double pos = (sorted.Count - 1) * Math.Clamp(p, 0, 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    /// <summary>
    /// 사정률 분포 막대. 가운데 95%가 들어가는 범위를 <paramref name="step"/>%p 간격으로 나누고,
    /// 바깥 값은 양 끝 막대에 넣는다 (드문 예가 초과 투찰 하나로 그래프가 납작해지지 않게).
    /// </summary>
    public static IReadOnlyList<RateBin> Histogram(IReadOnlyList<double> rates, double step = 0.1)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (rates.Count == 0) return [];
        var sorted = rates.Order().ToList();
        // 경계는 실제 값(가장 가까운 순위)으로 잡는다. 보간하면 값 하나뿐인 130% 같은 예가 초과 투찰이 범위를 끌어당긴다.
        int n = sorted.Count - 1;
        double from = Math.Floor(sorted[(int)Math.Ceiling(n * 0.025)] / step) * step;
        double to = Math.Floor(sorted[(int)Math.Floor(n * 0.975)] / step) * step + step;
        int bins = Math.Clamp((int)Math.Round((to - from) / step), 1, 60);
        // 값이 몇 개뿐이면 막대 하나가 폭을 다 채워 분포로 읽히지 않으니, 최소 MinBins개 폭을 값 주변으로 넓힌다.
        if (bins < MinBins)
        {
            int extra = MinBins - bins;
            from -= extra / 2 * step;
            bins = MinBins;
        }
        var counts = new int[bins];
        foreach (double r in sorted)
            counts[Math.Clamp((int)Math.Floor((r - from) / step), 0, bins - 1)]++;
        return [.. counts.Select((c, i) => new RateBin(Math.Round(from + i * step, 4), Math.Round(from + (i + 1) * step, 4), c))];
    }
}
