namespace BidNote.Application.Collecting;

public sealed record ApiUsageToday(string ApiName, int Count, int DailyLimit, DateTime? LastCalledAt)
{
    public double Ratio => DailyLimit <= 0 ? 0 : (double)Count / DailyLimit;
}

public sealed class ApiLimitOptions
{
    public const string SectionName = "ApiLimits";

    /// <summary>API별 설정이 없을 때의 하루 한도. 공공데이터포털 개발계정 기본값 수준.</summary>
    public int DefaultDaily { get; set; } = 1000;

    /// <summary>한도의 이 비율에 닿으면 더 호출하지 않는다 (예: 0.95 = 95%).</summary>
    public double StopAtRatio { get; set; } = 0.95;

    /// <summary>API 이름별 하루 한도. 활용신청 화면에 나온 값으로 바꿀 수 있다.</summary>
    public Dictionary<string, int> PerApi { get; set; } = [];

    public int LimitFor(string apiName) => PerApi.TryGetValue(apiName, out int v) ? v : DefaultDaily;
}

public interface IApiUsageTracker
{
    /// <summary>오늘 이 API를 더 불러도 되는지. 한도의 <see cref="ApiLimitOptions.StopAtRatio"/>에 닿으면 false.</summary>
    Task<bool> CanCallAsync(string apiName, CancellationToken cancellationToken);

    /// <summary>호출 한 번을 기록한다. HTTP 요청을 보낼 때마다 부른다.</summary>
    Task RecordAsync(string apiName, CancellationToken cancellationToken);

    /// <summary>오늘 호출한 API별 사용량. 설정에 한도가 있는 API는 호출이 없어도 0건으로 보여 준다.</summary>
    Task<IReadOnlyList<ApiUsageToday>> GetTodayAsync(CancellationToken cancellationToken);
}
