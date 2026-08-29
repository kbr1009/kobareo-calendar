using KobareoCalendar.Domain;

namespace KobareoCalendar.Application;

public sealed record ScheduleView(Guid Id, string Title, string? Details, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, bool IsPrivate, bool IsRedacted, Guid CreatedById, IReadOnlySet<Guid> ParticipantIds,
    IReadOnlySet<Guid> ResourceIds, int Lane, long Version);

public static class SchedulePrivacy
{
    public static ScheduleView ToView(Schedule value, Guid viewerId, bool isAdmin, int lane = 0)
    {
        var canSee = !value.IsPrivate || isAdmin || value.CreatedById == viewerId || value.ParticipantIds.Contains(viewerId);
        return new ScheduleView(value.Id, canSee ? value.Title : "非公開の予定", canSee ? value.Details : null,
            value.StartsAtUtc, value.EndsAtUtc, value.IsPrivate, !canSee, value.CreatedById,
            value.ParticipantIds, value.ResourceIds, lane, value.Version);
    }
}
