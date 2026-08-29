using KobareoCalendar.Application;
using KobareoCalendar.Domain;

namespace KobareoCalendar.Domain.Tests;

public class PrivacyTests
{
    [Fact]
    public void Private_content_is_redacted_for_unrelated_viewer()
    {
        var schedule = new Schedule
        {
            Title = "secret",
            Details = "hidden",
            IsPrivate = true,
            StartsAtUtc = DateTimeOffset.UtcNow,
            EndsAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            CreatedById = Guid.NewGuid(),
            ParticipantIds = [Guid.NewGuid()]
        };
        var view = SchedulePrivacy.ToView(schedule, Guid.NewGuid(), false);
        Assert.True(view.IsRedacted); Assert.Equal("非公開の予定", view.Title); Assert.Null(view.Details);
    }
}
