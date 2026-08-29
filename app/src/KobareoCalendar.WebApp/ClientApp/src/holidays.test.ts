import { describe, expect, it } from "vitest";
import { japaneseHolidays } from "./holidays";

describe("japaneseHolidays", () => {
  it("固定日、移動祝日、春分の日を返す", () => {
    const holidays = japaneseHolidays(2026);
    expect(holidays.get("2026-01-01")).toBe("元日");
    expect(holidays.get("2026-01-12")).toBe("成人の日");
    expect(holidays.get("2026-03-20")).toBe("春分の日");
  });

  it("日曜日の祝日に振替休日を設定する", () => {
    const holidays = japaneseHolidays(2026);
    expect(holidays.get("2026-05-04")).toBe("みどりの日");
    expect(holidays.get("2026-05-06")).toBe("振替休日");
  });
});
