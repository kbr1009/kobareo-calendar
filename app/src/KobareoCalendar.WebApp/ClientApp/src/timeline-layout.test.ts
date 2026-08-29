import { describe, expect, it } from "vitest";
import {
  allocateLanes,
  applyEventMutation,
  changeInterval,
  confirmConflictBody,
  snappedMinutes,
  stablePaletteIndex,
} from "./timeline-layout";
const event = (id: string, start: string, end: string) => ({
  id,
  startsAtUtc: `2026-08-15T${start}:00Z`,
  endsAtUtc: `2026-08-15T${end}:00Z`,
});
describe("allocateLanes", () => {
  it("境界が接する予定は同じレーンを再利用する", () => {
    const result = allocateLanes([
      event("a", "09:00", "10:00"),
      event("b", "10:00", "11:00"),
    ]);
    expect(result.map((x) => x.lane)).toEqual([0, 0]);
  });
  it("重複する予定は別レーンへ置く", () => {
    const result = allocateLanes([
      event("a", "09:00", "11:00"),
      event("b", "10:00", "12:00"),
    ]);
    expect(result.map((x) => x.lane)).toEqual([0, 1]);
  });
  it("終了後に最初の空きレーンを再利用する", () => {
    const result = allocateLanes([
      event("a", "09:00", "11:00"),
      event("b", "10:00", "12:00"),
      event("c", "11:00", "13:00"),
    ]);
    expect(result.map((x) => x.lane)).toEqual([0, 1, 0]);
  });
});
it("同じIDには範囲内の安定した色番号を返す", () => {
  const value = stablePaletteIndex("abc", 6);
  expect(value).toBe(stablePaletteIndex("abc", 6));
  expect(value).toBeGreaterThanOrEqual(0);
  expect(value).toBeLessThan(6);
});
describe("ドラッグによる時間変更", () => {
  const start = new Date("2026-08-15T09:00:00Z"),
    end = new Date("2026-08-15T10:00:00Z");
  it("ピクセル移動を5分単位へスナップする", () =>
    expect(snappedMinutes(10, 120)).toBe(5));
  it("タイムライン幅を取得できない場合は時間を変更しない", () => {
    expect(snappedMinutes(10, 0)).toBe(0);
    expect(snappedMinutes(10, Number.NaN)).toBe(0);
  });
  it("予定全体を同じ時間だけ移動する", () => {
    const changed = changeInterval(start, end, 60, "move");
    expect(changed.start.toISOString()).toContain("10:00:00");
    expect(changed.end.toISOString()).toContain("11:00:00");
  });
  it("リサイズ時は5分の最小時間を維持する", () =>
    expect(
      changeInterval(start, end, 120, "start").start.toISOString(),
    ).toContain("09:55:00"));
});
describe("保存結果の即時反映", () => {
  it("作成した予定を再読み込みなしで追加する", () =>
    expect(
      applyEventMutation([{ id: "a" }], { event: { id: "b" } }).map(
        (x) => x.id,
      ),
    ).toEqual(["a", "b"]));
  it("更新と削除をIDにより反映する", () => {
    expect(
      applyEventMutation([{ id: "a", title: "旧" }], {
        event: { id: "a", title: "新" },
      })[0].title,
    ).toBe("新");
    expect(applyEventMutation([{ id: "a" }], { deletedId: "a" })).toEqual([]);
  });
});
it("競合モーダルで続行すると明示承認フラグを付ける", () =>
  expect(confirmConflictBody({ title: "重複予定" })).toEqual({
    title: "重複予定",
    confirmConflicts: true,
  }));
