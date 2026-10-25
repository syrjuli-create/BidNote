using BidNote.Application.Collecting;
using BidNote.Application.Results;

namespace BidNote.Web.Services;

/// <summary>
/// 켜져 있는 동안 주기적으로 공고를 수집한다. 켤 때는 마지막으로 성공한 시각부터 이어 받아
/// PC가 꺼져 있던 동안 빠진 공고를 채운다. 주기·기간은 수집 설정 화면에서 바꾸면 다음 주기부터 적용된다.
/// 공고를 받은 뒤 같은 기간의 개찰 결과도 받는다 (실패해도 공고 수집은 성공으로 둔다).
/// </summary>
public sealed class CollectorHostedService(
    IServiceScopeFactory scopeFactory,
    CollectorState state,
    CollectorSignal signal,
    TimeProvider timeProvider,
    ILogger<CollectorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = await LoadSettingsAsync(stoppingToken);
        DateTime from = CollectorRules.StartFrom(timeProvider.GetLocalNow().DateTime, settings.LastSuccessTo, settings.BackfillDays);
        string trigger = settings.LastSuccessTo is null ? "자동(처음)" : "자동(이어 받기)";

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime to = timeProvider.GetLocalNow().DateTime;
            if (await RunAsync(from, to, trigger, settings.BackfillDays, stoppingToken)) from = to;

            settings = await LoadSettingsAsync(stoppingToken);
            int? backfillDays = await WaitAsync(TimeSpan.FromMinutes(settings.IntervalMinutes), stoppingToken);
            if (backfillDays is { } days)
            {
                // 다시 받기는 그 기간만 받고, 원래 이어 받던 위치(from)는 그대로 둔다.
                DateTime now = timeProvider.GetLocalNow().DateTime;
                await RunAsync(now.AddDays(-days), now, $"다시 받기({days}일)", days, stoppingToken);
            }
            trigger = "자동";
        }
    }

    /// <summary>주기만큼 기다린다. 그 사이 다시 받기 요청이 오면 바로 돌아와 그 일수를 준다.</summary>
    private async Task<int?> WaitAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var delay = Task.Delay(interval, timeProvider, cts.Token);
        var request = signal.Reader.WaitToReadAsync(cts.Token).AsTask();
        var done = await Task.WhenAny(delay, request);
        await cts.CancelAsync();
        stoppingToken.ThrowIfCancellationRequested();
        if (done == request && signal.Reader.TryRead(out int days)) return days;
        return null;
    }

    /// <summary>한 번 수집할 때 순위를 받아 둘 내 투찰·관심 공고 수. 공고마다 2번 이상 호출하므로 적게 둔다.</summary>
    private const int PendingRanksPerRun = 10;

    private async Task<bool> RunAsync(DateTime from, DateTime to, string trigger, int backfillDays, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<CollectService>();
            var result = await service.CollectAsync(from, to, stoppingToken);
            await scope.ServiceProvider.GetRequiredService<ICollectorSettingsStore>().MarkSuccessAsync(to, stoppingToken);
            logger.LogInformation("수집 결과({Trigger}): 새 공고 {Added}건, 변경 {Changed}건, 변화 없음 {Unchanged}건, 기초금액 {BasePrices}건, 원문 저장 {RawSaved}건",
                trigger, result.Added, result.Changed, result.Unchanged, result.BasePricesUpdated, result.RawSaved);
            state.Record(new CollectRun(to, trigger, result, null));
            await CollectRegionsAsync(scope.ServiceProvider, from, to, backfillDays, stoppingToken);
            await CollectResultsAsync(scope.ServiceProvider, from, to, backfillDays, stoppingToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 실패한 구간은 from을 그대로 두어 다음 주기에 다시 받는다.
            logger.LogError(ex, "공고 수집 실패({Trigger}): {From:yyyy-MM-dd HH:mm}~{To:yyyy-MM-dd HH:mm}. 다음 주기에 다시 시도합니다.", trigger, from, to);
            state.Record(new CollectRun(to, trigger, null, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// 개찰 결과를 받는다. 공고와 따로 실패할 수 있어 성공 시각을 따로 기록하고, 실패했으면 다음 수집 때
    /// 그 시각부터 다시 받는다 (<see cref="CollectorRules.CatchUpFrom"/>). 순위는 실패해도 다음 수집 때 다시 고른다.
    /// </summary>
    private async Task CollectResultsAsync(IServiceProvider services, DateTime from, DateTime to, int backfillDays, CancellationToken stoppingToken)
    {
        var results = services.GetRequiredService<IBidResultService>();
        var settingsStore = services.GetRequiredService<ICollectorSettingsStore>();
        try
        {
            var settings = await settingsStore.GetAsync(stoppingToken);
            from = CollectorRules.CatchUpFrom(from, to, settings.ResultsLastSuccessTo, await results.HasAnyAsync(stoppingToken), backfillDays);
            int updated = await results.CollectOpeningsAsync(from, to, stoppingToken);
            await settingsStore.MarkResultsSuccessAsync(to, stoppingToken);
            int ranks = await results.FetchPendingRanksAsync(PendingRanksPerRun, stoppingToken);
            logger.LogInformation("개찰 결과: {From:yyyy-MM-dd HH:mm}~{To:yyyy-MM-dd HH:mm} 반영 {Updated}건, 순위 받음 {Ranks}건", from, to, updated, ranks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "개찰 결과 수집 실패: {From:yyyy-MM-dd HH:mm}~{To:yyyy-MM-dd HH:mm}. 다음 수집 때 이어서 다시 받습니다.", from, to);
        }
    }

    /// <summary>참가 가능 지역을 받는다. 개찰 결과와 같은 방식으로 따로 성공 시각을 기록하고, 처음이면 백필 기간만큼 거슬러 받는다.</summary>
    private async Task CollectRegionsAsync(IServiceProvider services, DateTime from, DateTime to, int backfillDays, CancellationToken stoppingToken)
    {
        var regions = services.GetRequiredService<IRegionLimitService>();
        var settingsStore = services.GetRequiredService<ICollectorSettingsStore>();
        try
        {
            var settings = await settingsStore.GetAsync(stoppingToken);
            from = CollectorRules.CatchUpFrom(from, to, settings.RegionsLastSuccessTo, await regions.HasAnyAsync(stoppingToken), backfillDays);
            int updated = await regions.CollectAsync(from, to, stoppingToken);
            await settingsStore.MarkRegionsSuccessAsync(to, stoppingToken);
            logger.LogInformation("참가 가능 지역: {From:yyyy-MM-dd HH:mm}~{To:yyyy-MM-dd HH:mm} 반영 {Updated}건", from, to, updated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "참가 가능 지역 수집 실패: {From:yyyy-MM-dd HH:mm}~{To:yyyy-MM-dd HH:mm}. 다음 수집 때 이어서 다시 받습니다.", from, to);
        }
    }

    private async Task<CollectorSettingsValues> LoadSettingsAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICollectorSettingsStore>().GetAsync(stoppingToken);
    }
}
