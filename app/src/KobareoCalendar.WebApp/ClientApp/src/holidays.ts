export function japaneseHolidays(year: number) {
  const holidays = new Map<string, string>();
  const key = (month: number, day: number) =>
    `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
  const add = (month: number, day: number, name: string) =>
    holidays.set(key(month, day), name);
  const nthMonday = (month: number, nth: number) => {
    const first = new Date(year, month - 1, 1).getDay();
    return 1 + ((8 - first) % 7) + (nth - 1) * 7;
  };

  add(1, 1, "元日");
  add(1, nthMonday(1, 2), "成人の日");
  add(2, 11, "建国記念の日");
  if (year >= 2020) add(2, 23, "天皇誕生日");
  add(
    3,
    Math.floor(
      20.8431 +
        0.242194 * (year - 1980) -
        Math.floor((year - 1980) / 4),
    ),
    "春分の日",
  );
  add(4, 29, "昭和の日");
  add(5, 3, "憲法記念日");
  add(5, 4, "みどりの日");
  add(5, 5, "こどもの日");
  if (year === 2020) {
    add(7, 23, "海の日");
    add(7, 24, "スポーツの日");
    add(8, 10, "山の日");
  } else if (year === 2021) {
    add(7, 22, "海の日");
    add(7, 23, "スポーツの日");
    add(8, 8, "山の日");
  } else {
    add(7, nthMonday(7, 3), "海の日");
    add(8, 11, "山の日");
    add(10, nthMonday(10, 2), "スポーツの日");
  }
  add(9, nthMonday(9, 3), "敬老の日");
  add(
    9,
    Math.floor(
      23.2488 +
        0.242194 * (year - 1980) -
        Math.floor((year - 1980) / 4),
    ),
    "秋分の日",
  );
  add(11, 3, "文化の日");
  add(11, 23, "勤労感謝の日");

  for (let month = 1; month <= 12; month++) {
    const days = new Date(year, month, 0).getDate();
    for (let day = 2; day < days; day++) {
      const current = key(month, day);
      const previous = key(month, day - 1);
      const next = key(month, day + 1);
      if (
        !holidays.has(current) &&
        holidays.has(previous) &&
        holidays.has(next)
      ) {
        holidays.set(current, "国民の休日");
      }
    }
  }

  for (const [holidayKey] of [...holidays]) {
    const holiday = new Date(`${holidayKey}T12:00:00+09:00`);
    if (holiday.getDay() !== 0) continue;
    const substitute = new Date(holiday);
    do substitute.setDate(substitute.getDate() + 1);
    while (
      holidays.has(
        substitute.toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" }),
      )
    );
    holidays.set(
      substitute.toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" }),
      "振替休日",
    );
  }

  return holidays;
}
