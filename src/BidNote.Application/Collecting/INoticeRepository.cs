using BidNote.Domain.Collecting;
using BidNote.Domain.Notices;

namespace BidNote.Application.Collecting;

public interface INoticeRepository
{
    Task<Notice?> FindAsync(string sourceSystem, BusinessType businessType, string bidNo, CancellationToken cancellationToken);

    void Add(Notice notice);

    void AddRaw(ApiRawResponse raw);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
