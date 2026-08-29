namespace KobareoCalendar.Domain;

public enum ResourceKind { Room, Equipment }
public enum UserRole { Admin, User }

public sealed record ScheduleTarget(Guid Id, string Name, string Kind);

public sealed class Schedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string Details { get; set; } = "";
    public required DateTimeOffset StartsAtUtc { get; set; }
    public required DateTimeOffset EndsAtUtc { get; set; }
    public bool IsPrivate { get; set; }
    public required Guid CreatedById { get; init; }
    public HashSet<Guid> ParticipantIds { get; init; } = [];
    public HashSet<Guid> ResourceIds { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 200)
            throw new DomainException("題名は1〜200文字で入力してください。");
        if (EndsAtUtc <= StartsAtUtc)
            throw new DomainException("終了日時は開始日時より後にしてください。");
        if (Details.Length > 4000)
            throw new DomainException("詳細は4000文字以内で入力してください。");
        if (ParticipantIds.Count == 0)
            throw new DomainException("対象ユーザーを1人以上選択してください。");
    }

    public bool Overlaps(Schedule other) =>
        StartsAtUtc < other.EndsAtUtc && other.StartsAtUtc < EndsAtUtc;

    public bool SharesTarget(Schedule other) =>
        ParticipantIds.Overlaps(other.ParticipantIds) || ResourceIds.Overlaps(other.ResourceIds);
}

public sealed class DomainException(string message) : Exception(message);

public static class TimelineLaneAllocator
{
    public static IReadOnlyDictionary<Guid, int> Allocate(IEnumerable<Schedule> schedules)
    {
        var laneEnds = new List<DateTimeOffset>();
        var result = new Dictionary<Guid, int>();
        foreach (var item in schedules.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.EndsAtUtc))
        {
            var lane = laneEnds.FindIndex(end => end <= item.StartsAtUtc);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(item.EndsAtUtc); }
            else laneEnds[lane] = item.EndsAtUtc;
            result[item.Id] = lane;
        }
        return result;
    }
}

public static class ConflictDecision
{
    public static bool MustWarn(int conflictCount, bool explicitlyConfirmed) => conflictCount > 0 && !explicitlyConfirmed;
}
