export type TimelineInterval = {
  id: string;
  startsAtUtc: string;
  endsAtUtc: string;
};
export type PositionedInterval<T> = { event: T; lane: number };

export function allocateLanes<T extends TimelineInterval>(
  events: T[],
): PositionedInterval<T>[] {
  const laneEnds: number[] = [];
  return [...events]
    .sort(
      (a, b) =>
        Date.parse(a.startsAtUtc) - Date.parse(b.startsAtUtc) ||
        Date.parse(a.endsAtUtc) - Date.parse(b.endsAtUtc),
    )
    .map((event) => {
      const start = Date.parse(event.startsAtUtc),
        end = Date.parse(event.endsAtUtc);
      let lane = laneEnds.findIndex((value) => value <= start);
      if (lane < 0) {
        lane = laneEnds.length;
        laneEnds.push(end);
      } else laneEnds[lane] = end;
      return { event, lane };
    });
}

export function stablePaletteIndex(id: string, paletteSize: number) {
  let hash = 0;
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) | 0;
  return (hash >>> 0) % paletteSize;
}

export type IntervalChangeMode = "move" | "start" | "end";
export function snappedMinutes(deltaPixels: number, pixelsPerHour: number) {
  if (!Number.isFinite(pixelsPerHour) || pixelsPerHour <= 0) return 0;
  return Math.round(deltaPixels / (pixelsPerHour / 12)) * 5;
}
export function changeInterval(
  start: Date,
  end: Date,
  deltaMinutes: number,
  mode: IntervalChangeMode,
) {
  const delta = deltaMinutes * 60000,
    minimum = 5 * 60000;
  if (mode === "move")
    return {
      start: new Date(start.getTime() + delta),
      end: new Date(end.getTime() + delta),
    };
  if (mode === "start")
    return {
      start: new Date(
        Math.min(start.getTime() + delta, end.getTime() - minimum),
      ),
      end,
    };
  return {
    start,
    end: new Date(Math.max(end.getTime() + delta, start.getTime() + minimum)),
  };
}
export function applyEventMutation<T extends { id: string }>(
  events: T[],
  result: { event?: T; deletedId?: string },
) {
  if (result.deletedId)
    return events.filter((event) => event.id !== result.deletedId);
  if (result.event)
    return [
      ...events.filter((event) => event.id !== result.event!.id),
      result.event,
    ];
  return events;
}
export function confirmConflictBody<T extends object>(body: T) {
  return { ...body, confirmConflicts: true as const };
}
