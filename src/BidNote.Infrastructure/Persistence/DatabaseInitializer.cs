using System.Text.Json;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.G2B;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BidNote.Infrastructure.Persistence;

/// <summary>시작할 때 DB를 최신 구조로 맞춘다.</summary>
public static class DatabaseInitializer
{
    /// <param name="purgeFakeData">실제 모드면 true. 가상 공고와 그 원문을 지운다 (API 사용량 기록은 남긴다).</param>
    public static async Task InitializeAsync(BidNoteDbContext db, StorageLocation storage, bool purgeFakeData, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(storage);
        BackUpLegacyDatabase(storage.DatabasePath, logger);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        if (purgeFakeData) await PurgeFakeDataAsync(db, logger, cancellationToken).ConfigureAwait(false);
        await RestoreWipedBasePricesAsync(db, logger, cancellationToken).ConfigureAwait(false);
        await ShortenRegionNamesAsync(db, logger, cancellationToken).ConfigureAwait(false);
        await FillLicenseDetailsAsync(db, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 면허 제한 줄의 허용 업종·주력분야(2026-10-25 추가)를 저장된 공고 원문에서 읽어 채운다.
    /// 아직 읽지 않은 줄(Alternatives나 MainFields가 null)만 다루므로 한 번 채우면 다시 돌지 않는다. 원문이 없으면 빈 값으로 둔다.
    /// 허용 업종이 있는 줄 수를 돌려준다.
    /// </summary>
    public static async Task<int> FillLicenseDetailsAsync(BidNoteDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.NoticeLicenses.Include(x => x.Notice).Where(x => x.Alternatives == null || x.MainFields == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) return 0;

        int filled = 0, withMainFields = 0;
        foreach (var notice in rows.GroupBy(x => x.Notice!))
        {
            string? raw = await db.ApiRawResponses.AsNoTracking()
                .Where(r => r.SourceSystem == notice.Key.SourceSystem && r.BidNo == notice.Key.BidNo && r.ApiName == G2BCollector.ListApi)
                .OrderByDescending(r => r.Id).Select(r => r.ResponseJson).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var fromRaw = new Dictionary<(int, string), (string Alternatives, string MainFields)>();
            if (raw is not null)
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("licenses", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var l in list.EnumerateArray().Select(G2BCollector.MapLicense))
                        fromRaw[(l.GroupNo, l.Name)] = (NoticeLicense.JoinAlternatives(l.Alternatives ?? []), NoticeLicense.JoinMainFields(l.MainFields ?? []));
                }
            }
            foreach (var row in notice)
            {
                var (alternatives, mainFields) = fromRaw.GetValueOrDefault((row.GroupNo, row.Name), ("", ""));
                row.Alternatives = alternatives;
                row.MainFields = mainFields;
                if (alternatives.Length > 0) filled++;
                if (mainFields.Length > 0) withMainFields++;
            }
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("면허 제한 {Rows}줄의 허용 업종·주력분야를 원문에서 읽었습니다 (허용 업종 있음 {Filled}줄, 주력분야 있음 {MainFields}줄).",
            rows.Count, filled, withMainFields);
        return filled;
    }

    /// <summary>예전에 "대전광역시"처럼 긴 이름으로 저장한 지역을 짧은 이름("대전")으로 고친다.</summary>
    public static async Task<int> ShortenRegionNamesAsync(BidNoteDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        int changed = 0;
        foreach (string name in RegionNames.LongNames)
        {
            string shortName = RegionNames.ToShort(name);
            changed += await db.Notices.Where(x => x.Region == name)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Region, shortName), cancellationToken).ConfigureAwait(false);
        }
        if (changed > 0) logger.LogInformation("지역 이름 {Count}건을 짧은 이름으로 고쳤습니다.", changed);
        return changed;
    }

    /// <summary>
    /// 정정 공고가 기초금액을 빈 값으로 덮어쓰던 버그(2026-10-19 수정)로 지워진 기초금액을
    /// 저장해 둔 기초금액 원문에서 다시 읽어 되살린다. 해당 공고가 없으면 아무것도 하지 않는다.
    /// </summary>
    public static async Task<int> RestoreWipedBasePricesAsync(BidNoteDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var wiped = await db.Notices.Where(x => x.BasePrice == null && x.BasePriceHash != "").ToListAsync(cancellationToken).ConfigureAwait(false);
        int restored = 0;
        foreach (var n in wiped)
        {
            string? raw = await db.ApiRawResponses.AsNoTracking()
                .Where(r => r.SourceSystem == n.SourceSystem && r.BidNo == n.BidNo && r.ApiName == G2BCollector.BasePriceApi)
                .OrderByDescending(r => r.Id).Select(r => r.ResponseJson).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (raw is null) continue;
            using var doc = JsonDocument.Parse(raw);
            if (G2BCollector.MapBasePrice(doc.RootElement, "") is not { } p) continue;
            n.BasePrice = p.BasePrice;
            n.AValue = p.AValue;
            n.NetCost = p.NetCost;
            restored++;
        }
        if (restored > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("지워졌던 기초금액 {Count}건을 저장된 원문에서 되살렸습니다.", restored);
        }
        return restored;
    }

    /// <summary>가상 공고(SourceSystem=FAKE)만 지운다. 변경 이력·면허는 공고와 함께 지워진다.</summary>
    public static async Task PurgeFakeDataAsync(BidNoteDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        int notices = await db.Notices.Where(x => x.SourceSystem == SourceSystems.Fake).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        int raws = await db.ApiRawResponses.Where(x => x.SourceSystem == SourceSystems.Fake).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (notices + raws > 0) logger.LogInformation("실제 모드로 바뀌어 가상 공고 {Notices}건, 가상 원문 {Raws}건을 지웠습니다.", notices, raws);
    }

    /// <summary>
    /// 마이그레이션 도입 전(EnsureCreated)에 만든 DB는 이력 표가 없어 갱신할 수 없다.
    /// 지우지 않고 이름을 바꿔 두고 새 DB로 시작한다.
    /// </summary>
    private static void BackUpLegacyDatabase(string path, ILogger logger)
    {
        if (!File.Exists(path)) return;

        bool legacy;
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT (SELECT count(*) FROM sqlite_master WHERE name = 'Notices') > 0 AND (SELECT count(*) FROM sqlite_master WHERE name = '__EFMigrationsHistory') = 0";
            legacy = Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        if (!legacy) return;

        SqliteConnection.ClearAllPools();
        string backup = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.legacy-{DateTime.Now:yyyyMMddHHmmss}.db");
        File.Move(path, backup);
        foreach (string suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix)) File.Move(path + suffix, backup + suffix);
        }
        logger.LogWarning("마이그레이션 이전 DB를 {Backup}(으)로 옮기고 새 DB를 만듭니다.", backup);
    }
}
