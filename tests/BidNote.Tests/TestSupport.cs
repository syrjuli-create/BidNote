using BidNote.Application.Collecting;
using BidNote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Tests;

/// <summary>현재 시각을 고정하는 시계. 현지 시각 = UTC로 둔다.</summary>
public sealed class TestClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>테스트마다 새로 만드는 메모리 SQLite DB.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public TestDb()
    {
        connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public BidNoteDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BidNoteDbContext>().UseSqlite(connection).Options);

    public void Dispose() => connection.Dispose();
}

/// <summary>한도를 따지지 않고 호출 수만 세는 사용량 기록기.</summary>
public sealed class AlwaysAllowUsage : IApiUsageTracker
{
    public int Recorded { get; private set; }
    public Task<bool> CanCallAsync(string apiName, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task RecordAsync(string apiName, CancellationToken cancellationToken) { Recorded++; return Task.CompletedTask; }
    public Task<IReadOnlyList<ApiUsageToday>> GetTodayAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApiUsageToday>>([]);
}
