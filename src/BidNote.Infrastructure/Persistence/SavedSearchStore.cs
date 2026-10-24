using BidNote.Application.Home;
using BidNote.Domain.Searching;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

/// <summary>검색 조건 저장. 화면 버튼에서 불리므로 호출마다 DB 연결을 새로 연다.</summary>
public sealed class SavedSearchStore(IDbContextFactory<BidNoteDbContext> factory, TimeProvider timeProvider) : ISavedSearchStore
{
    public async Task<IReadOnlyList<SavedSearchItem>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.SavedSearches.AsNoTracking()
            .Select(x => new SavedSearchItem(x.Id, x.Name, x.Query, x.UpdatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.OrderBy(x => x.Name, StringComparer.CurrentCulture)];
    }

    public async Task<SavedSearchItem> SaveAsync(string name, string query, CancellationToken cancellationToken)
    {
        string title = (name ?? "").Trim();
        if (title.Length > SavedSearch.MaxNameLength) title = title[..SavedSearch.MaxNameLength];
        if (title.Length == 0) throw new ArgumentException("이름을 넣어 주세요.", nameof(name));
        string normalized = SearchQuery.Normalize(query);
        if (normalized.Length == 0) throw new ArgumentException("저장할 검색 조건이 없습니다.", nameof(query));
        if (normalized.Length > SavedSearch.MaxQueryLength) throw new ArgumentException("검색 조건이 너무 깁니다.", nameof(query));

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTime now = timeProvider.GetLocalNow().DateTime;
        var row = await db.SavedSearches.FirstOrDefaultAsync(x => x.Name == title, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new SavedSearch { Name = title, CreatedAt = now };
            db.SavedSearches.Add(row);
        }
        row.Query = normalized;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SavedSearchItem(row.Id, row.Name, row.Query, row.UpdatedAt);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.SavedSearches.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetLastAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.LastSearches.Where(x => x.Id == LastSearch.SingletonId).Select(x => x.Query)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? "";
    }

    public async Task SetLastAsync(string query, CancellationToken cancellationToken)
    {
        string normalized = SearchQuery.Normalize(query);
        if (normalized.Length > SavedSearch.MaxQueryLength) return;
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.LastSearches.FirstOrDefaultAsync(x => x.Id == LastSearch.SingletonId, cancellationToken).ConfigureAwait(false);
        if (normalized.Length == 0)
        {
            if (row is not null) db.LastSearches.Remove(row);
        }
        else if (row is null)
        {
            db.LastSearches.Add(new LastSearch { Query = normalized, UpdatedAt = timeProvider.GetLocalNow().DateTime });
        }
        else if (row.Query != normalized)
        {
            row.Query = normalized;
            row.UpdatedAt = timeProvider.GetLocalNow().DateTime;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
