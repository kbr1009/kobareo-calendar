using KobareoCalendar.Domain;

namespace KobareoCalendar.Domain.Tests;

public class SchedulingTests
{
    private static Schedule At(int start, int end) => new()
    {
        Title = "test",
        StartsAtUtc = DateTimeOffset.Parse($"2026-08-14T{start:00}:00:00Z"),
        EndsAtUtc = DateTimeOffset.Parse($"2026-08-14T{end:00}:00:00Z"),
        CreatedById = Guid.NewGuid(),
        ParticipantIds = [Guid.Parse("11111111-1111-1111-1111-111111111111")]
    };

    [Fact] public void Rejects_end_before_start() => Assert.Throws<DomainException>(() => At(10, 9).Validate());
    [Fact] public void Touching_boundaries_do_not_overlap() => Assert.False(At(9, 10).Overlaps(At(10, 11)));
    [Fact] public void Intersecting_ranges_overlap() => Assert.True(At(9, 11).Overlaps(At(10, 12)));
    [Fact] public void Conflict_requires_warning_without_confirmation() => Assert.True(ConflictDecision.MustWarn(1, false));
    [Fact] public void Explicit_confirmation_allows_conflict() => Assert.False(ConflictDecision.MustWarn(1, true));

    [Fact]
    public void Allocator_reuses_lane_after_event_ends()
    {
        var a = At(9, 11); var b = At(10, 12); var c = At(11, 13);
        var lanes = TimelineLaneAllocator.Allocate([a, b, c]);
        Assert.Equal(0, lanes[a.Id]); Assert.Equal(1, lanes[b.Id]); Assert.Equal(0, lanes[c.Id]);
    }
}
