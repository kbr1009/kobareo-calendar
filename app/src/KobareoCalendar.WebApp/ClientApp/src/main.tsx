import React, { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { createRoot } from "react-dom/client";
import {
  Avatar,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  FluentProvider,
  Input,
  webDarkTheme,
  webLightTheme,
} from "@fluentui/react-components";
import {
  Add20Regular,
  AddCircle20Regular,
  Archive20Regular,
  ArrowLeft20Regular,
  ArrowUndo20Regular,
  Building20Regular,
  CalendarToday20Regular,
  ChevronLeft20Regular,
  ChevronRight20Regular,
  Clock20Regular,
  Dismiss20Regular,
  Key20Regular,
  LockClosed20Regular,
  PanelLeftContract20Regular,
  PanelLeftExpand20Regular,
  People20Regular,
  Person20Regular,
  Save20Regular,
  Search20Regular,
  Settings20Regular,
  SignOut20Regular,
  Warning20Regular,
  WeatherMoon20Regular,
  WeatherSunny20Regular,
} from "@fluentui/react-icons";
import {
  allocateLanes,
  applyEventMutation,
  changeInterval,
  confirmConflictBody,
  snappedMinutes,
  stablePaletteIndex,
  type IntervalChangeMode,
} from "./timeline-layout";
import { japaneseHolidays } from "./holidays";
import "./styles.css";

type User = {
  id: string;
  displayName: string;
  email: string;
  role: "Admin" | "User";
  isActive: boolean;
};
type Resource = {
  id: string;
  name: string;
  kind: "Room" | "Equipment";
  isActive: boolean;
  version: number;
};
type PasswordPolicy = {
  requiredLength: number;
  requireDigit: boolean;
  requireLowercase: boolean;
  requireUppercase: boolean;
  requireNonAlphanumeric: boolean;
};
type LoginAttemptPolicy = {
  isEnabled: boolean;
  maxFailedAttempts: number;
};
type Event = {
  id: string;
  title: string;
  details?: string;
  startsAtUtc: string;
  endsAtUtc: string;
  isPrivate: boolean;
  isRedacted: boolean;
  createdById: string;
  participantIds: string[];
  resourceIds: string[];
  lane: number;
  version: number;
};
type CalendarView = "timeline" | "month";
type TimelineTarget = {
  kind: "User" | "Resource";
  id: string;
  isVisible: boolean;
};
type NotificationItem = {
  id: string;
  actorDisplayName: string;
  scheduleTitle: string;
  scheduleId: string;
  createdAtUtc: string;
  isRead: boolean;
};
type NotificationList = {
  unreadCount: number;
  items: NotificationItem[];
};
const hours = Array.from({ length: 13 }, (_, i) => i + 8);
class ApiFailure extends Error {
  constructor(
    message: string,
    public status: number,
    public data: any,
  ) {
    super(message);
  }
}
let csrfToken = "";
async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const method = (init?.method || "GET").toUpperCase();
  if (!["GET", "HEAD", "OPTIONS"].includes(method) && !csrfToken) {
    const csrf = await fetch("/api/auth/csrf").then((r) => r.json());
    csrfToken = csrf.token;
  }
  const r = await fetch("/api" + path, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(csrfToken ? { "X-CSRF-TOKEN": csrfToken } : {}),
      ...init?.headers,
    },
  });
  if (!r.ok) {
    const data = await r.json().catch(() => null);
    throw new ApiFailure(
      data?.detail || "通信に失敗しました。",
      r.status,
      data,
    );
  }
  return r.status === 204 ? (undefined as T) : r.json();
}
function useScheduleRealtime(
  onChanged: () => void,
  setConnected: (connected: boolean) => void,
) {
  const callback = useRef(onChanged);
  callback.current = onChanged;
  useEffect(() => {
    let stopped = false,
      socket: WebSocket | undefined,
      retry = 0,
      timer: number | undefined;
    const connect = () => {
      const protocol = location.protocol === "https:" ? "wss:" : "ws:";
      socket = new WebSocket(`${protocol}//${location.host}/api/realtime`);
      socket.onopen = () => {
        retry = 0;
        setConnected(true);
      };
      socket.onmessage = (event) => {
        try {
          const message = JSON.parse(event.data);
          if (message.type === "schedule.changed") callback.current();
        } catch {}
      };
      socket.onclose = () => {
        setConnected(false);
        if (!stopped)
          timer = window.setTimeout(
            connect,
            Math.min(30000, 1000 * 2 ** retry++),
          );
      };
      socket.onerror = () => socket?.close();
    };
    connect();
    return () => {
      stopped = true;
      window.clearTimeout(timer);
      socket?.close();
      setConnected(false);
    };
  }, []);
}

type ColorMode = "light" | "dark";

const themeStorageKey = "kobareo-calendar-color-mode";
const kobareoLightTheme = {
  ...webLightTheme,
  colorBrandBackground: "#041e5d",
  colorBrandBackgroundHover: "#0b347f",
  colorBrandBackgroundPressed: "#020f30",
};
const kobareoDarkTheme = {
  ...webDarkTheme,
  colorBrandBackground: "#041e5d",
  colorBrandBackgroundHover: "#0b347f",
  colorBrandBackgroundPressed: "#020f30",
};

function App({ colorMode, toggleColorMode }: ThemeControlProps) {
  const [me, setMe] = useState<User | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    api<User>("/auth/me")
      .then(setMe)
      .catch(() => {});
  }, []);
  if (!me)
    return (
      <Login
        onLogin={setMe}
        error={error}
        setError={setError}
        colorMode={colorMode}
        toggleColorMode={toggleColorMode}
      />
    );
  return (
    <Calendar
      me={me}
      colorMode={colorMode}
      toggleColorMode={toggleColorMode}
      onLogout={async () => {
        await api("/auth/logout", { method: "POST" });
        setMe(null);
      }}
    />
  );
}
function Login({
  onLogin,
  error,
  setError,
  colorMode,
  toggleColorMode,
}: {
  onLogin: (u: User) => void;
  error: string;
  setError: (s: string) => void;
} & ThemeControlProps) {
  const [email, setEmail] = useState(""),
    [password, setPassword] = useState("");
  return (
    <main className="login">
      <ThemeToggle
        colorMode={colorMode}
        toggleColorMode={toggleColorMode}
        className="loginThemeToggle"
      />
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setError("");
          try {
            const user = await api<User>("/auth/login", {
              method: "POST",
              body: JSON.stringify({ email, password }),
            });
            csrfToken = "";
            onLogin(user);
          } catch (x) {
            setError((x as Error).message);
          }
        }}
      >
        <h1>kobareo-calendar</h1>
        <p>予定を、ひと目で。</p>
        <label>
          メールアドレス
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="username"
          />
        </label>
        <label>
          パスワード
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
          />
        </label>
        {error && (
          <div role="alert" className="alert">
            {error}
          </div>
        )}
        <button>ログイン</button>
      </form>
    </main>
  );
}
type ThemeControlProps = {
  colorMode: ColorMode;
  toggleColorMode: () => void;
};

function ThemeToggle({
  colorMode,
  toggleColorMode,
  className,
}: ThemeControlProps & { className?: string }) {
  const dark = colorMode === "dark";
  return (
    <Button
      className={className}
      appearance="subtle"
      icon={dark ? <WeatherSunny20Regular /> : <WeatherMoon20Regular />}
      onClick={toggleColorMode}
      aria-label={dark ? "ライトモードに切り替え" : "ダークモードに切り替え"}
    />
  );
}

function Calendar({
  me,
  onLogout,
  colorMode,
  toggleColorMode,
}: { me: User; onLogout: () => void } & ThemeControlProps) {
  const [date, setDate] = useState(new Date()),
    [users, setUsers] = useState<User[]>([]),
    [resources, setResources] = useState<Resource[]>([]),
    [events, setEvents] = useState<Event[]>([]),
    [monthEvents, setMonthEvents] = useState<Event[]>([]),
    [view, setView] = useState<CalendarView>("timeline"),
    [realtimeConnected, setRealtimeConnected] = useState(false),
    [timelineTargets, setTimelineTargets] = useState<TimelineTarget[]>([]),
    [notifications, setNotifications] = useState<NotificationList>({
      unreadCount: 0,
      items: [],
    }),
    [notificationsExpanded, setNotificationsExpanded] = useState(false),
    [pendingHighlightId, setPendingHighlightId] = useState<string | null>(null),
    [highlightedEventId, setHighlightedEventId] = useState<string | null>(null),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [showAdmin, setShowAdmin] = useState(false),
    [dialog, setDialog] = useState<Event | "new" | null>(null),
    [draftHour, setDraftHour] = useState(9),
    [sidebarOpen, setSidebarOpen] = useState(
      () => !window.matchMedia("(max-width: 600px)").matches,
    ),
    [targetSearch, setTargetSearch] = useState(""),
    [moveConflict, setMoveConflict] = useState<{
      event: Event;
      start: Date;
      end: Date;
      conflicts: Event[];
    } | null>(null);
  const highlightTimer = useRef<number | undefined>(undefined);
  const selected = timelineTargets
    .filter((target) => target.isVisible)
    .map((target) => target.id);
  const day = date.toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" });
  useEffect(() => {
    const mobile = window.matchMedia("(max-width: 600px)"),
      closeOnMobile = (event: MediaQueryListEvent) => {
        if (event.matches) setSidebarOpen(false);
      };
    mobile.addEventListener("change", closeOnMobile);
    return () => mobile.removeEventListener("change", closeOnMobile);
  }, []);
  const rows = useMemo(
    () => [
      { id: me.id, name: me.displayName, kind: "自分" },
      ...selected.flatMap((id) => {
        const user = users.find((x) => x.id === id);
        if (user)
          return [
            {
              id: user.id,
              name: user.displayName,
              kind: "ユーザー",
              archived: !user.isActive,
            },
          ];
        const resource = resources.find((x) => x.id === id);
        return resource
          ? [
              {
                id: resource.id,
                name: resource.name,
                kind: resource.kind === "Room" ? "会議室" : "備品",
                archived: !resource.isActive,
              },
            ]
          : [];
      }),
    ],
    [me, users, resources, selected],
  );
  const loadEvents = (ids: string[]) =>
    api<Event[]>(
      `/schedules?from=${day}T00:00:00%2B09:00&to=${day}T23:59:59%2B09:00&targets=${[me.id, ...ids].join(",")}`,
    ).then(setEvents);
  const loadMonth = () => {
    const year = date.getFullYear(),
      month = date.getMonth(),
      from = `${year}-${String(month + 1).padStart(2, "0")}-01T00:00:00%2B09:00`,
      next = new Date(year, month + 1, 1),
      to = `${next.getFullYear()}-${String(next.getMonth() + 1).padStart(2, "0")}-01T00:00:00%2B09:00`;
    return api<Event[]>(
      `/schedules?from=${from}&to=${to}&targets=${me.id}`,
    ).then(setMonthEvents);
  };
  const load = () => {
    setLoading(true);
    return Promise.all([
      api<{ users: User[]; resources: Resource[] }>("/targets"),
      api<{ targets: TimelineTarget[] }>("/timeline-preferences"),
      api<NotificationList>("/notifications"),
    ])
      .then(async ([t, p, notificationList]) => {
        const targets = p.targets.filter((x) => x.id !== me.id);
        const ids = targets
          .filter((target) => target.isVisible)
          .map((target) => target.id);
        setUsers(t.users);
        setResources(t.resources);
        setTimelineTargets(targets);
        setNotifications(notificationList);
        await loadEvents(ids);
        setError("");
      })
      .catch((x) => setError(x.message))
      .finally(() => setLoading(false));
  };
  useEffect(() => {
    load();
  }, [day]);
  useEffect(() => {
    if (!pendingHighlightId || !events.some((event) => event.id === pendingHighlightId))
      return;
    setHighlightedEventId(pendingHighlightId);
    setPendingHighlightId(null);
    window.clearTimeout(highlightTimer.current);
    highlightTimer.current = window.setTimeout(
      () => setHighlightedEventId(null),
      2200,
    );
  }, [events, pendingHighlightId]);
  useEffect(
    () => () => window.clearTimeout(highlightTimer.current),
    [],
  );
  useEffect(() => {
    if (view === "month") loadMonth().catch((x) => setError(x.message));
  }, [view, date.getFullYear(), date.getMonth()]);
  useScheduleRealtime(() => {
    loadEvents(selected).catch(() => {});
    if (view === "month") loadMonth().catch(() => {});
    api<NotificationList>("/notifications")
      .then(setNotifications)
      .catch(() => {});
  }, setRealtimeConnected);
  const saveTimelineTargets = async (targets: TimelineTarget[]) => {
    try {
      const saved = await api<{ targets: TimelineTarget[] }>(
        "/timeline-preferences",
        {
          method: "PUT",
          body: JSON.stringify({ targets }),
        },
      );
      const registered = saved.targets.filter((target) => target.id !== me.id);
      setTimelineTargets(registered);
      await loadEvents(
        registered
          .filter((target) => target.isVisible)
          .map((target) => target.id),
      );
    } catch (x) {
      setError((x as Error).message);
    }
  };
  const toggleTarget = (id: string) =>
    saveTimelineTargets(
      timelineTargets.map((target) =>
        target.id === id
          ? { ...target, isVisible: !target.isVisible }
          : target,
      ),
    );
  const addTarget = (kind: "User" | "Resource", id: string) =>
    saveTimelineTargets([
      ...timelineTargets,
      { kind, id, isVisible: true },
    ]);
  const removeTarget = (id: string) =>
    saveTimelineTargets(timelineTargets.filter((target) => target.id !== id));
  const openNotification = async (notification: NotificationItem) => {
    try {
      if (!notification.isRead) {
        await api("/notifications/read", {
          method: "POST",
          body: JSON.stringify({ ids: [notification.id] }),
        });
        setNotifications((current) => ({
          unreadCount: Math.max(0, current.unreadCount - 1),
          items: current.items.map((item) =>
            item.id === notification.id ? { ...item, isRead: true } : item,
          ),
        }));
      }
      const schedule = await api<Event>(`/schedules/${notification.scheduleId}`);
      setView("timeline");
      setDate(new Date(schedule.startsAtUtc));
      setPendingHighlightId(schedule.id);
      if (window.matchMedia("(max-width: 600px)").matches)
        setSidebarOpen(false);
      setError("");
    } catch (reason) {
      setError((reason as Error).message);
    }
  };
  const changeEvent = async (
    event: Event,
    startsAtUtc: Date,
    endsAtUtc: Date,
    confirmed = false,
  ): Promise<void> => {
    const body = {
      title: event.title,
      details: event.details ?? "",
      startsAtUtc: startsAtUtc.toISOString(),
      endsAtUtc: endsAtUtc.toISOString(),
      isPrivate: event.isPrivate,
      participantIds: event.participantIds,
      resourceIds: event.resourceIds,
      confirmConflicts: confirmed,
      version: event.version,
    };
    try {
      const updated = await api<Event>("/schedules/" + event.id, {
        method: "PUT",
        body: JSON.stringify(body),
      });
      setEvents((current) =>
        current.map((x) => (x.id === updated.id ? updated : x)),
      );
      setMoveConflict(null);
      setError("");
    } catch (x) {
      if (
        !confirmed &&
        x instanceof ApiFailure &&
        x.status === 409 &&
        x.data?.conflicts
      ) {
        setMoveConflict({
          event,
          start: startsAtUtc,
          end: endsAtUtc,
          conflicts: x.data.conflicts,
        });
        return;
      }
      setError((x as Error).message);
      throw x;
    }
  };
  const applyMutation = (result: { event?: Event; deletedId?: string }) => {
    setDialog(null);
    setEvents((current) => applyEventMutation(current, result));
    setMonthEvents((current) => applyEventMutation(current, result));
  };
  const openSlot = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect(),
      hour = Math.max(
        8,
        Math.min(
          19.5,
          8 + Math.floor(((e.clientX - rect.left) / rect.width) * 144) / 12,
        ),
      );
    setDraftHour(hour);
    setDialog("new");
  };
  const nowMarker = useCurrentTimeMarker(day),
    needle = targetSearch.trim().toLocaleLowerCase("ja-JP"),
    visibleUsers = users.filter(
      (x) =>
        needle.length > 0 &&
        x.isActive &&
        x.id !== me.id &&
        !timelineTargets.some((target) => target.id === x.id) &&
        x.displayName.toLocaleLowerCase("ja-JP").includes(needle),
    ),
    visibleResources = resources.filter(
      (x) =>
        needle.length > 0 &&
        x.isActive &&
        !timelineTargets.some((target) => target.id === x.id) &&
        x.name.toLocaleLowerCase("ja-JP").includes(needle),
    );
  const registeredUsers = timelineTargets.flatMap((target) => {
      if (target.kind !== "User") return [];
      const user = users.find((candidate) => candidate.id === target.id);
      return user ? [{ target, name: user.displayName }] : [];
    }),
    registeredResources = timelineTargets.flatMap((target) => {
      if (target.kind !== "Resource") return [];
      const resource = resources.find((candidate) => candidate.id === target.id);
      return resource ? [{ target, name: resource.name }] : [];
    });
  const movePeriod = (amount: number) =>
    setDate((current) =>
      view === "month"
        ? new Date(current.getFullYear(), current.getMonth() + amount, 1)
        : new Date(current.getTime() + amount * 86400000),
    );
  if (showAdmin)
    return (
      <AdminPanel
        me={me}
        colorMode={colorMode}
        toggleColorMode={toggleColorMode}
        close={() => {
          setShowAdmin(false);
          load();
        }}
        onLogout={onLogout}
      />
    );
  return (
    <div className="outlookApp">
      <header className="appBar">
        <div className="brand">
          {view === "timeline" && (
            <Button
              appearance="subtle"
              className="panelToggle"
              icon={
                sidebarOpen ? (
                  <PanelLeftContract20Regular />
                ) : (
                  <PanelLeftExpand20Regular />
                )
              }
              onClick={() => setSidebarOpen((x) => !x)}
              aria-label={sidebarOpen ? "表示対象を隠す" : "表示対象を表示"}
            />
          )}
          <img className="appLogo" src="/calendar-logo.svg" alt="kobareo-calendar" />
          <span className="appSection">予定表</span>
        </div>
        <div className="account">
          <ThemeToggle
            colorMode={colorMode}
            toggleColorMode={toggleColorMode}
          />
          <span
            className={`liveStatus ${realtimeConnected ? "connected" : ""}`}
            aria-label={realtimeConnected ? "リアルタイム同期中" : "再接続中"}
          >
            {realtimeConnected ? "ライブ" : "接続中…"}
          </span>
          {me.role === "Admin" && (
            <Button
              appearance="subtle"
              icon={<Settings20Regular />}
              onClick={() => setShowAdmin(true)}
            >
              管理
            </Button>
          )}
          <span className="accountAvatar">
            <Avatar name={me.displayName} size={28} />
            {notifications.unreadCount > 0 && (
              <span
                className="notificationBadge"
                aria-label={`${notifications.unreadCount}件の未読通知`}
              >
                {notifications.unreadCount > 99
                  ? "99+"
                  : notifications.unreadCount}
              </span>
            )}
          </span>
          <span>{me.displayName}</span>
          <Button
            appearance="subtle"
            icon={<SignOut20Regular />}
            onClick={onLogout}
            aria-label="ログアウト"
          />
        </div>
      </header>
      <main
        className={`shell ${sidebarOpen ? "" : "sidebarClosed"} ${view === "month" ? "calendarMode" : ""}`}
      >
        <aside aria-label="表示対象">
          <NotificationPanel
            notifications={notifications}
            expanded={notificationsExpanded}
            setExpanded={setNotificationsExpanded}
            openNotification={openNotification}
            markAllRead={async () => {
              try {
                await api("/notifications/read", {
                  method: "POST",
                  body: JSON.stringify({ ids: null }),
                });
                setNotifications((current) => ({
                  unreadCount: 0,
                  items: current.items.map((item) => ({
                    ...item,
                    isRead: true,
                  })),
                }));
              } catch (reason) {
                setError((reason as Error).message);
              }
            }}
          />
          <div className="paneTitle">
            <People20Regular />
            <h2>表示対象</h2>
          </div>
          {sidebarOpen && (
            <>
              <Input
                className="targetSearch"
                size="small"
                placeholder="ユーザー・設備を検索"
                value={targetSearch}
                onChange={(_, data) => setTargetSearch(data.value)}
              />
              <RegisteredTargetGroup
                title="ユーザー"
                items={registeredUsers}
                toggle={toggleTarget}
                remove={removeTarget}
              />
              <RegisteredTargetGroup
                title="会議室・備品"
                items={registeredResources}
                toggle={toggleTarget}
                remove={removeTarget}
              />
              {needle && (
                <TargetSearchResults
                  users={visibleUsers}
                  resources={visibleResources}
                  add={addTarget}
                />
              )}
            </>
          )}
        </aside>
        <section className="content">
          <div className="commandBar">
            <div className="dateNavigation">
              <Button
                appearance="subtle"
                icon={<ChevronLeft20Regular />}
                onClick={() => movePeriod(-1)}
                aria-label={view === "month" ? "前月" : "前日"}
              />
              <Button
                appearance="secondary"
                onClick={() => setDate(new Date())}
              >
                今日
              </Button>
              <input
                className="datePicker"
                aria-label="表示日"
                type="date"
                value={day}
                onChange={(e) =>
                  setDate(new Date(e.target.value + "T12:00:00+09:00"))
                }
              />
              <Button
                appearance="subtle"
                icon={<ChevronRight20Regular />}
                onClick={() => movePeriod(1)}
                aria-label={view === "month" ? "翌月" : "翌日"}
              />
              <h1>
                {view === "month"
                  ? date.toLocaleDateString("ja-JP", {
                      year: "numeric",
                      month: "long",
                    })
                  : date.toLocaleDateString("ja-JP", {
                      timeZone: "Asia/Tokyo",
                      month: "long",
                      day: "numeric",
                      weekday: "short",
                    })}
              </h1>
            </div>
            <div className="commandActions">
              <div
                className="viewSwitch"
                role="group"
                aria-label="予定表の表示形式"
              >
                <Button
                  className="responsiveCommandButton"
                  appearance={view === "timeline" ? "primary" : "subtle"}
                  icon={<Clock20Regular />}
                  onClick={() => setView("timeline")}
                  aria-pressed={view === "timeline"}
                  aria-label="タイムライン表示"
                >
                  <span className="responsiveButtonLabel">タイムライン</span>
                </Button>
                <Button
                  className="responsiveCommandButton"
                  appearance={view === "month" ? "primary" : "subtle"}
                  icon={<CalendarToday20Regular />}
                  onClick={() => setView("month")}
                  aria-pressed={view === "month"}
                  aria-label="カレンダー表示"
                >
                  <span className="responsiveButtonLabel">カレンダー</span>
                </Button>
              </div>
              <Button
                className="responsiveCommandButton"
                appearance="primary"
                icon={<Add20Regular />}
                onClick={() => {
                  setDraftHour(9);
                  setDialog("new");
                }}
                aria-label="新しい予定を作成"
              >
                <span className="responsiveButtonLabel">新しい予定</span>
              </Button>
            </div>
          </div>
          {error && (
            <div role="alert" className="alert">
              {error}
            </div>
          )}
          {view === "month" ? (
            <MonthCalendar
              date={date}
              events={monthEvents}
              me={me}
              openEvent={setDialog}
              createOnDay={(selectedDate) => {
                setDate(selectedDate);
                setDraftHour(9);
                setDialog("new");
              }}
            />
          ) : (
            <>
              {loading && (
                <div className="loading" role="status" aria-live="polite">
                  予定を読み込んでいます…
                </div>
              )}
              {!loading && !error && events.length === 0 && (
                <p className="empty">この日の予定はありません。</p>
              )}
              <p className="hint">
                予定を5分単位でドラッグ移動・時間変更できます。空き時間のダブルクリックでも作成できます。
              </p>
              <div className="timeline" aria-busy={loading}>
                <div className="timeHead">
                  <div className="nameCell">対象</div>
                  <div className="grid">
                    {hours.map((h) => (
                      <span
                        key={h}
                        style={{ left: `${((h - 8) / 12) * 100}%` }}
                      >
                        {h}:00
                      </span>
                    ))}
                  </div>
                </div>
                {rows.map((row) => (
                  <TimelineRow
                    key={row.id}
                    row={row}
                    events={events}
                    users={users}
                    resources={resources}
                    day={day}
                    nowMarker={nowMarker}
                    openSlot={openSlot}
                    openEvent={setDialog}
                    canEdit={(event) => event.createdById === me.id}
                    changeEvent={changeEvent}
                    highlightedEventId={highlightedEventId}
                  />
                ))}
              </div>
            </>
          )}
        </section>
      </main>
      {dialog && (
        <ScheduleDialog
          value={dialog}
          day={day}
          initialHour={draftHour}
          me={me}
          users={users}
          resources={resources}
          close={() => setDialog(null)}
          saved={applyMutation}
        />
      )}
      <ConflictDialog
        open={moveConflict !== null}
        conflicts={moveConflict?.conflicts ?? []}
        actionLabel="重複したまま変更"
        onCancel={() => setMoveConflict(null)}
        onContinue={() =>
          moveConflict &&
          changeEvent(
            moveConflict.event,
            moveConflict.start,
            moveConflict.end,
            true,
          )
        }
      />
    </div>
  );
}
function NotificationPanel({
  notifications,
  expanded,
  setExpanded,
  openNotification,
  markAllRead,
}: {
  notifications: NotificationList;
  expanded: boolean;
  setExpanded: (expanded: boolean) => void;
  openNotification: (notification: NotificationItem) => Promise<void>;
  markAllRead: () => Promise<void>;
}) {
  const visible = expanded
    ? notifications.items
    : notifications.items.slice(0, 5);
  return (
    <section className="notificationPanel" aria-label="通知">
      <div className="notificationHeading">
        <strong>通知</strong>
        {notifications.unreadCount > 0 && (
          <button type="button" onClick={() => markAllRead()}>
            すべて既読
          </button>
        )}
      </div>
      {visible.length === 0 ? (
        <p className="notificationEmpty">新しい通知はありません</p>
      ) : (
        <div className="notificationItems">
          {visible.map((notification) => (
            <button
              type="button"
              key={notification.id}
              className={notification.isRead ? "read" : "unread"}
              onClick={() => openNotification(notification)}
              aria-label={`${notification.actorDisplayName}さんが登録した「${notification.scheduleTitle}」の予定へ移動`}
            >
              <Avatar name={notification.actorDisplayName} size={24} />
              <p>
                <strong>{notification.actorDisplayName}</strong>さんが
                「{notification.scheduleTitle}」を登録しました
                <time dateTime={notification.createdAtUtc}>
                  {new Date(notification.createdAtUtc).toLocaleString("ja-JP", {
                    timeZone: "Asia/Tokyo",
                    month: "numeric",
                    day: "numeric",
                    hour: "2-digit",
                    minute: "2-digit",
                  })}
                </time>
              </p>
            </button>
          ))}
        </div>
      )}
      {notifications.items.length > 5 && (
        <button
          type="button"
          className="notificationMore"
          onClick={() => setExpanded(!expanded)}
        >
          {expanded ? "折りたたむ" : `ほか${notifications.items.length - 5}件を表示`}
        </button>
      )}
    </section>
  );
}

function RegisteredTargetGroup({
  title,
  items,
  toggle,
  remove,
}: {
  title: string;
  items: { target: TimelineTarget; name: string }[];
  toggle: (id: string) => void;
  remove: (id: string) => void;
}) {
  return (
    <fieldset>
      <legend>{title}</legend>
      {items.length === 0 ? (
        <small className="targetEmpty">検索して追加してください</small>
      ) : (
        items.map((item) => (
          <div className="registeredTarget" key={item.target.id}>
            <label>
              <input
                type="checkbox"
                checked={item.target.isVisible}
                onChange={() => toggle(item.target.id)}
              />
              <span>{item.name}</span>
            </label>
            <button
              type="button"
              onClick={() => remove(item.target.id)}
              aria-label={`${item.name}を表示対象から削除`}
            >
              <Dismiss20Regular />
            </button>
          </div>
        ))
      )}
    </fieldset>
  );
}

function TargetSearchResults({
  users,
  resources,
  add,
}: {
  users: User[];
  resources: Resource[];
  add: (kind: "User" | "Resource", id: string) => void;
}) {
  const results = [
    ...users.map((user) => ({
      kind: "User" as const,
      id: user.id,
      name: user.displayName,
      description: "ユーザー",
    })),
    ...resources.map((resource) => ({
      kind: "Resource" as const,
      id: resource.id,
      name: resource.name,
      description: resource.kind === "Room" ? "会議室" : "備品",
    })),
  ];
  return (
    <section className="targetResults" aria-label="検索結果">
      <strong>検索結果</strong>
      {results.length === 0 ? (
        <p>一致する対象はありません</p>
      ) : (
        results.slice(0, 20).map((result) => (
          <button
            type="button"
            key={result.id}
            onClick={() => add(result.kind, result.id)}
          >
            <AddCircle20Regular />
            <span>
              {result.name}
              <small>{result.description}</small>
            </span>
          </button>
        ))
      )}
    </section>
  );
}

function MonthCalendar({
  date,
  events,
  me,
  openEvent,
  createOnDay,
}: {
  date: Date;
  events: Event[];
  me: User;
  openEvent: (event: Event) => void;
  createOnDay: (date: Date) => void;
}) {
  const year = date.getFullYear(),
    month = date.getMonth(),
    first = new Date(year, month, 1),
    start = new Date(year, month, 1 - first.getDay()),
    today = new Date().toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" }),
    holidaysByYear = new Map([
      [year - 1, japaneseHolidays(year - 1)],
      [year, japaneseHolidays(year)],
      [year + 1, japaneseHolidays(year + 1)],
    ]),
    days = Array.from(
      { length: 42 },
      (_, index) =>
        new Date(
          start.getFullYear(),
          start.getMonth(),
          start.getDate() + index,
        ),
    );
  return (
    <div
      className="monthCalendar"
      aria-label={`${year}年${month + 1}月の自分の予定`}
    >
      <div className="weekdays" aria-hidden="true">
        {["日", "月", "火", "水", "木", "金", "土"].map((label) => (
          <span key={label}>{label}</span>
        ))}
      </div>
      <div className="monthGrid">
        {days.map((cell) => {
          const key = `${cell.getFullYear()}-${String(cell.getMonth() + 1).padStart(2, "0")}-${String(cell.getDate()).padStart(2, "0")}`,
            cellStart = Date.parse(key + "T00:00:00+09:00"),
            cellEnd = cellStart + 86400000,
            items = events.filter(
              (event) =>
                event.participantIds.includes(me.id) &&
                Date.parse(event.startsAtUtc) < cellEnd &&
                cellStart < Date.parse(event.endsAtUtc),
            ),
            holiday = holidaysByYear.get(cell.getFullYear())?.get(key);
          return (
            <section
              key={key}
              className={`monthDay ${cell.getMonth() !== month ? "outside" : ""} ${key === today ? "today" : ""} ${holiday ? "holiday" : ""}`}
              role="button"
              tabIndex={0}
              aria-label={`${key}${holiday ? ` ${holiday}` : ""}に予定を作成`}
              onClick={() =>
                createOnDay(
                  new Date(
                    cell.getFullYear(),
                    cell.getMonth(),
                    cell.getDate(),
                    12,
                  ),
                )
              }
              onKeyDown={(event) => {
                if (event.key !== "Enter" && event.key !== " ") return;
                event.preventDefault();
                createOnDay(
                  new Date(
                    cell.getFullYear(),
                    cell.getMonth(),
                    cell.getDate(),
                    12,
                  ),
                );
              }}
            >
              <header>
                <time dateTime={key}>{cell.getDate()}</time>
                {key === today && <span>今日</span>}
                {holiday && <span className="holidayName">{holiday}</span>}
              </header>
              <div className="monthEvents">
                {items.map((event) => (
                  <button
                    key={event.id}
                    className={`monthEvent tone${stablePaletteIndex(event.id, 6)} ${event.createdById === me.id ? "editable" : "readonly"}`}
                    onClick={(click) => {
                      click.stopPropagation();
                      if (event.createdById === me.id) openEvent(event);
                    }}
                    aria-disabled={event.createdById !== me.id}
                    aria-label={`${event.title} ${new Date(event.startsAtUtc).toLocaleTimeString("ja-JP", { timeZone: "Asia/Tokyo", hour: "2-digit", minute: "2-digit" })}`}
                  >
                    <time>
                      {new Date(event.startsAtUtc).toLocaleTimeString("ja-JP", {
                        timeZone: "Asia/Tokyo",
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </time>
                    <span>{event.title}</span>
                  </button>
                ))}
              </div>
            </section>
          );
        })}
      </div>
    </div>
  );
}
function currentTimeMarker(day: string, now = new Date()) {
  if (now.toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" }) !== day)
    return null;
  const parts = new Intl.DateTimeFormat("en-US", {
      timeZone: "Asia/Tokyo",
      hour12: false,
      hour: "2-digit",
      minute: "2-digit",
    }).formatToParts(now),
    hour = Number(parts.find((x) => x.type === "hour")?.value),
    minute = Number(parts.find((x) => x.type === "minute")?.value),
    value = hour + minute / 60;
  if (value < 8 || value > 20) return null;
  return {
    left: `${((value - 8) / 12) * 100}%`,
    label: `${String(hour).padStart(2, "0")}:${String(minute).padStart(2, "0")}`,
  };
}
function useCurrentTimeMarker(day: string) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    setNow(new Date());
    const timer = window.setInterval(() => setNow(new Date()), 60000);
    return () => window.clearInterval(timer);
  }, [day]);
  return currentTimeMarker(day, now);
}
function TimelineRow({
  row,
  events,
  users,
  resources,
  day,
  nowMarker,
  openSlot,
  openEvent,
  canEdit,
  changeEvent,
  highlightedEventId,
}: {
  row: { id: string; name: string; kind: string; archived?: boolean };
  events: Event[];
  users: User[];
  resources: Resource[];
  day: string;
  nowMarker: { left: string; label: string } | null;
  openSlot: (e: React.MouseEvent<HTMLDivElement>) => void;
  openEvent: (event: Event) => void;
  canEdit: (event: Event) => boolean;
  changeEvent: (event: Event, start: Date, end: Date) => Promise<void>;
  highlightedEventId: string | null;
}) {
  const rowEvents = events.filter(
      (e) =>
        e.participantIds.includes(row.id) || e.resourceIds.includes(row.id),
    ),
    positioned = allocateLanes(rowEvents),
    laneCount = Math.max(1, ...positioned.map((x) => x.lane + 1)),
    height = 18 + laneCount * 43;
  return (
    <div
      className="row"
      style={
        {
          minHeight: Math.max(72, height),
          "--mobile-row-height": `${1 + laneCount * 33}px`,
        } as React.CSSProperties
      }
    >
      <div className="nameCell">
        <Avatar name={row.name} size={24} />
        <span>
          <small>{row.kind}</small>
          {row.name}
          {row.archived && <em className="archivedBadge">アーカイブ済み</em>}
        </span>
      </div>
      <div
        className="grid"
        onDoubleClick={openSlot}
        aria-label={`${row.name}の空き時間`}
      >
        {hours.map((h) => (
          <i key={h} style={{ left: `${((h - 8) / 12) * 100}%` }} />
        ))}
        {nowMarker && (
          <em
            className="nowLine"
            style={{ left: nowMarker.left }}
            data-time={nowMarker.label}
            aria-label={`現在時刻 ${nowMarker.label}`}
          />
        )}{" "}
        {positioned.map(({ event, lane }) => (
          <EventBlock
            key={event.id}
            event={event}
            users={users}
            resources={resources}
            lane={lane}
            editable={canEdit(event)}
            overlapping={positioned.some(
              (x) =>
                x.event.id !== event.id &&
                Date.parse(x.event.startsAtUtc) < Date.parse(event.endsAtUtc) &&
                Date.parse(event.startsAtUtc) < Date.parse(x.event.endsAtUtc),
            )}
            day={day}
            open={() => openEvent(event)}
            change={changeEvent}
            highlighted={event.id === highlightedEventId}
          />
        ))}
      </div>
    </div>
  );
}
function EventBlock({
  event: e,
  users,
  resources,
  lane,
  overlapping,
  editable,
  day,
  open,
  change,
  highlighted,
}: {
  event: Event;
  users: User[];
  resources: Resource[];
  lane: number;
  overlapping: boolean;
  editable: boolean;
  day: string;
  open: () => void;
  change: (event: Event, start: Date, end: Date) => Promise<void>;
  highlighted: boolean;
}) {
  const [preview, setPreview] = useState<{ start: Date; end: Date } | null>(
      null,
    ),
    [saving, setSaving] = useState(false),
    [hoverOpen, setHoverOpen] = useState(false),
    [hoverPoint, setHoverPoint] = useState({ x: 0, y: 0 });
  const suppressClick = useRef(false),
    hoverTimer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(hoverTimer.current), []);
  const actualStart = preview?.start ?? new Date(e.startsAtUtc),
    actualEnd = preview?.end ?? new Date(e.endsAtUtc),
    rangeStart = new Date(day + "T08:00:00+09:00"),
    rangeEnd = new Date(day + "T20:00:00+09:00"),
    start = new Date(Math.max(actualStart.getTime(), rangeStart.getTime())),
    end = new Date(Math.min(actualEnd.getTime(), rangeEnd.getTime()));
  if (end <= start) return null;
  const min = `${((start.getTime() - rangeStart.getTime()) / (rangeEnd.getTime() - rangeStart.getTime())) * 100}%`,
    width = `${((end.getTime() - start.getTime()) / (rangeEnd.getTime() - rangeStart.getTime())) * 100}%`,
    startLabel = actualStart.toLocaleTimeString("ja-JP", {
      timeZone: "Asia/Tokyo",
      hour: "2-digit",
      minute: "2-digit",
    }),
    endLabel = actualEnd.toLocaleTimeString("ja-JP", {
      timeZone: "Asia/Tokyo",
      hour: "2-digit",
      minute: "2-digit",
    }),
    showHover = (ev: React.MouseEvent) => {
      setHoverPoint({ x: ev.clientX, y: ev.clientY });
      window.clearTimeout(hoverTimer.current);
      hoverTimer.current = window.setTimeout(() => setHoverOpen(true), 320);
    },
    hideHover = () => {
      window.clearTimeout(hoverTimer.current);
      setHoverOpen(false);
    };
  const begin = (pointer: React.PointerEvent, mode: IntervalChangeMode) => {
    if (!editable || saving || pointer.button !== 0) return;
    hideHover();
    pointer.preventDefault();
    pointer.stopPropagation();
    const originX = pointer.clientX,
      timeline = (pointer.currentTarget as HTMLElement).closest<HTMLElement>(
        ".grid",
      ),
      pixelsPerHour = (timeline?.getBoundingClientRect().width ?? 0) / 12,
      originalStart = new Date(e.startsAtUtc),
      originalEnd = new Date(e.endsAtUtc);
    let latest = { start: originalStart, end: originalEnd },
      changed = false;
    const move = (ev: PointerEvent) => {
      const delta = snappedMinutes(ev.clientX - originX, pixelsPerHour);
      changed = delta !== 0;
      latest = changeInterval(originalStart, originalEnd, delta, mode);
      setPreview(latest);
    };
    const up = async () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      if (!changed) {
        setPreview(null);
        return;
      }
      suppressClick.current = true;
      setSaving(true);
      try {
        await change(e, latest.start, latest.end);
      } catch {
      } finally {
        setPreview(null);
        setSaving(false);
        window.setTimeout(() => {
          suppressClick.current = false;
        }, 0);
      }
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up, { once: true });
  };
  const participantNames = e.participantIds
      .map((id) => {
        const user = users.find((candidate) => candidate.id === id);
        return user
          ? `${user.displayName}${user.isActive ? "" : "（アーカイブ済み）"}`
          : undefined;
      })
      .filter(Boolean) as string[],
    resourceNames = e.resourceIds
      .map((id) => {
        const resource = resources.find((candidate) => candidate.id === id);
        return resource
          ? `${resource.name}${resource.isActive ? "" : "（アーカイブ済み）"}`
          : undefined;
      })
      .filter(Boolean) as string[],
    cardLeft = Math.max(
      12,
      Math.min(hoverPoint.x + 20, window.innerWidth - 372),
    ),
    cardPosition: React.CSSProperties =
      hoverPoint.y + 300 < window.innerHeight
        ? { left: cardLeft, top: hoverPoint.y + 22 }
        : { left: cardLeft, bottom: window.innerHeight - hoverPoint.y + 22 };
  const hoverCard = (
    <div
      className="eventHoverCard fixedHoverCard"
      style={cardPosition}
      role="tooltip"
    >
      <div className="hoverCardTitle">
        {e.isPrivate && <LockClosed20Regular />}
        <strong>{e.title}</strong>
      </div>
      <div className="hoverMeta">
        <Clock20Regular />
        <span>
          {actualStart.toLocaleDateString("ja-JP", {
            timeZone: "Asia/Tokyo",
            month: "short",
            day: "numeric",
            weekday: "short",
          })}{" "}
          {startLabel}–{endLabel}
        </span>
      </div>
      {e.details && <p className="hoverDetails">{e.details}</p>}
      {participantNames.length > 0 && (
        <div className="hoverMeta">
          <People20Regular />
          <span>{participantNames.join("、")}</span>
        </div>
      )}
      {resourceNames.length > 0 && (
        <div className="hoverMeta">
          <Building20Regular />
          <span>{resourceNames.join("、")}</span>
        </div>
      )}
      <div className="hoverHint">
        {editable
          ? "クリックして編集 ・ ドラッグで時間を変更"
          : "閲覧のみ（作成者だけが変更できます）"}
      </div>
    </div>
  );
  return (
    <>
      <button
        onMouseEnter={showHover}
        onMouseLeave={hideHover}
        onFocus={(ev) => {
          const rect = ev.currentTarget.getBoundingClientRect();
          setHoverPoint({ x: rect.right, y: rect.top });
          setHoverOpen(true);
        }}
        onBlur={hideHover}
        onPointerDown={(ev) => {
          if (!(ev.target as HTMLElement).closest(".resizeHandle"))
            begin(ev, "move");
        }}
        onClick={(ev) => {
          hideHover();
          if (!editable) {
            ev.preventDefault();
            return;
          }
          if (suppressClick.current) {
            ev.preventDefault();
            return;
          }
          open();
        }}
        onDoubleClick={(ev) => ev.stopPropagation()}
        className={`event tone${stablePaletteIndex(e.id, 6)} ${editable ? "editable" : "readonly"} ${saving ? "saving" : ""} ${e.isPrivate ? "private" : ""} ${overlapping ? "overlapping" : ""} ${highlighted ? "notificationHighlight" : ""}`}
        style={
          {
            left: min,
            width,
            "--event-top": `${7 + lane * 43}px`,
            "--mobile-packed-top": `${3 + lane * 33}px`,
          } as React.CSSProperties
        }
        aria-disabled={!editable}
        aria-label={`${e.title}、${startLabel}から${endLabel}${e.isPrivate ? "、非公開" : ""}${overlapping ? "、重複予定あり" : ""}`}
      >
        {editable && (
          <span
            className="resizeHandle startHandle"
            onPointerDown={(ev) => begin(ev, "start")}
            aria-hidden="true"
          />
        )}
        {overlapping && (
          <span className="overlapMark" aria-hidden="true">
            ⇅
          </span>
        )}
        <b>{e.title}</b>
        <span>
          {startLabel}–{endLabel}
        </span>
        {editable && (
          <span
            className="resizeHandle endHandle"
            onPointerDown={(ev) => begin(ev, "end")}
            aria-hidden="true"
          />
        )}
      </button>
      {hoverOpen && createPortal(hoverCard, document.body)}
    </>
  );
}
function localValue(value: string | Date) {
  return new Intl.DateTimeFormat("sv-SE", {
    timeZone: "Asia/Tokyo",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  })
    .format(new Date(value))
    .replace(" ", "T");
}
function tokyoInputToIso(value: FormDataEntryValue | null) {
  return new Date(String(value) + ":00+09:00").toISOString();
}
function ScheduleDialog({
  value,
  day,
  initialHour,
  me,
  users,
  resources,
  close,
  saved,
}: {
  value: Event | "new";
  day: string;
  initialHour: number;
  me: User;
  users: User[];
  resources: Resource[];
  close: () => void;
  saved: (result: { event?: Event; deletedId?: string }) => void;
}) {
  const fresh = value === "new",
    event = fresh ? null : value,
    editable = fresh || event?.createdById === me.id;
  const [error, setError] = useState(""),
    [conflict, setConflict] = useState<{
      body: any;
      conflicts: Event[];
    } | null>(null),
    [deleteConfirmationOpen, setDeleteConfirmationOpen] = useState(false),
    [saving, setSaving] = useState(false);
  useEffect(() => {
    document
      .querySelectorAll<HTMLInputElement>(
        '.dateFields input[type="datetime-local"]',
      )
      .forEach((input) => {
        input.step = "300";
      });
  }, [value]);
  const persist = async (body: any, confirmed = false) => {
    setSaving(true);
    try {
      const request = confirmed
        ? confirmConflictBody(body)
        : { ...body, confirmConflicts: false };
      const updated = await api<Event>(
        fresh ? "/schedules" : "/schedules/" + event!.id,
        { method: fresh ? "POST" : "PUT", body: JSON.stringify(request) },
      );
      setConflict(null);
      saved({ event: updated });
    } catch (x) {
      if (
        !confirmed &&
        x instanceof ApiFailure &&
        x.status === 409 &&
        x.data?.conflicts
      ) {
        setConflict({ body, conflicts: x.data.conflicts });
        setError("");
        return;
      }
      setError((x as Error).message);
    } finally {
      setSaving(false);
    }
  };
  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget),
      body = {
        title: f.get("title"),
        details: f.get("details"),
        startsAtUtc: tokyoInputToIso(f.get("start")),
        endsAtUtc: tokyoInputToIso(f.get("end")),
        isPrivate: f.get("private") === "on",
        participantIds: f.getAll("participants"),
        resourceIds: f.getAll("resources"),
        version: event?.version ?? 0,
      };
    await persist(body);
  };
  const remove = async () => {
    if (!event) return;
    setSaving(true);
    try {
      await api(`/schedules/${event.id}?version=${event.version}`, {
        method: "DELETE",
      });
      setDeleteConfirmationOpen(false);
      saved({ deletedId: event.id });
    } catch (x) {
      setError((x as Error).message);
      setDeleteConfirmationOpen(false);
    } finally {
      setSaving(false);
    }
  };
  const time = (hour: number) => {
      const total = Math.round(hour * 60),
        hours = Math.floor(total / 60),
        minutes = total % 60;
      return `${day}T${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`;
    },
    start = event ? localValue(event.startsAtUtc) : time(initialHour),
    end = event ? localValue(event.endsAtUtc) : time(initialHour + 0.5);
  return (
    <>
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="schedule-title"
      >
        <form className="dialog" onSubmit={submit}>
          <div className="dialogHead">
            <h2 id="schedule-title">{fresh ? "新しい予定" : "予定詳細"}</h2>
            <button type="button" onClick={close} aria-label="閉じる">
              ×
            </button>
          </div>
          {error && (
            <div role="alert" className="alert">
              {error}
            </div>
          )}
          <label>
            題名
            <input
              name="title"
              defaultValue={event?.title || ""}
              disabled={!editable || event?.isRedacted}
              required
              maxLength={200}
            />
          </label>
          <label>
            詳細
            <textarea
              name="details"
              defaultValue={event?.details || ""}
              disabled={!editable || event?.isRedacted}
              maxLength={4000}
            />
          </label>
          <div className="dateFields">
            <label>
              開始
              <input
                name="start"
                type="datetime-local"
                step="900"
                defaultValue={start}
                disabled={!editable}
                required
              />
            </label>
            <label>
              終了
              <input
                name="end"
                type="datetime-local"
                step="900"
                defaultValue={end}
                disabled={!editable}
                required
              />
            </label>
          </div>
          <label>
            <input
              name="private"
              type="checkbox"
              defaultChecked={event?.isPrivate}
              disabled={!editable}
            />
            非公開
          </label>
          <fieldset disabled={!editable}>
            <legend>対象ユーザー</legend>
            {users
              .filter(
                (u) =>
                  u.isActive ||
                  (!fresh && event!.participantIds.includes(u.id)),
              )
              .map((u) => (
                <label key={u.id}>
                  <input
                    name="participants"
                    type="checkbox"
                    value={u.id}
                    defaultChecked={
                      fresh
                        ? u.id === me.id
                        : event!.participantIds.includes(u.id)
                    }
                  />
                  {u.displayName}
                  {!u.isActive && "（アーカイブ済み）"}
                </label>
              ))}
          </fieldset>
          <fieldset disabled={!editable}>
            <legend>会議室・備品</legend>
            {resources
              .filter(
                (resource) =>
                  resource.isActive ||
                  (!fresh && event!.resourceIds.includes(resource.id)),
              )
              .map((resource) => (
                <label key={resource.id}>
                  <input
                    name="resources"
                    type="checkbox"
                    value={resource.id}
                    defaultChecked={event?.resourceIds.includes(resource.id)}
                  />
                  {resource.name}
                  {!resource.isActive && "（アーカイブ済み）"}
                </label>
              ))}
          </fieldset>
          <div className="actions">
            <button type="button" onClick={close}>
              閉じる
            </button>
            {editable && !fresh && (
              <button
                type="button"
                className="danger"
                onClick={() => setDeleteConfirmationOpen(true)}
              >
                削除
              </button>
            )}
            {editable && (
              <button className="primary" disabled={saving}>
                {saving ? "保存中…" : fresh ? "登録" : "保存"}
              </button>
            )}
          </div>
        </form>
      </div>
      <ConflictDialog
        open={conflict !== null}
        conflicts={conflict?.conflicts ?? []}
        actionLabel="重複したまま登録"
        busy={saving}
        onCancel={() => setConflict(null)}
        onContinue={() => conflict && persist(conflict.body, true)}
      />
      <Dialog
        open={deleteConfirmationOpen}
        modalType="alert"
        onOpenChange={(_, data) => {
          if (!data.open && !saving) setDeleteConfirmationOpen(false);
        }}
      >
        <DialogSurface className="deleteScheduleDialog">
          <DialogBody>
            <DialogTitle>予定を削除</DialogTitle>
            <DialogContent>
              <p>
                「<strong>{event?.title}</strong>」を削除しますか？
              </p>
              <p>この操作は取り消せません。</p>
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => setDeleteConfirmationOpen(false)}
                disabled={saving}
              >
                キャンセル
              </Button>
              <Button
                className="deleteConfirmButton"
                appearance="primary"
                onClick={remove}
                disabled={saving}
              >
                {saving ? "削除中…" : "削除する"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
}

function ConflictDialog({
  open,
  conflicts,
  actionLabel,
  onCancel,
  onContinue,
  busy = false,
}: {
  open: boolean;
  conflicts: Event[];
  actionLabel: string;
  onCancel: () => void;
  onContinue: () => void;
  busy?: boolean;
}) {
  return (
    <Dialog open={open} modalType="alert">
      <DialogSurface className="conflictDialog">
        <DialogBody>
          <DialogTitle>
            <span className="warningTitle">
              <Warning20Regular />
              予定が重複しています
            </span>
          </DialogTitle>
          <DialogContent>
            <p>
              選択した対象には、同じ時間帯に別の予定があります。それでも続行しますか？
            </p>
            <ul>
              {conflicts.map((item) => (
                <li key={item.id}>
                  <strong>{item.title}</strong>
                  <span>
                    {new Date(item.startsAtUtc).toLocaleString("ja-JP", {
                      timeZone: "Asia/Tokyo",
                    })}
                    ～
                    {new Date(item.endsAtUtc).toLocaleTimeString("ja-JP", {
                      timeZone: "Asia/Tokyo",
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                  </span>
                </li>
              ))}
            </ul>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onCancel} disabled={busy}>
              戻って修正
            </Button>
            <Button appearance="primary" onClick={onContinue} disabled={busy}>
              {busy ? "登録中…" : actionLabel}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
function AdminPanel({
  me,
  close,
  onLogout,
  colorMode,
  toggleColorMode,
}: {
  me: User;
  close: () => void;
  onLogout: () => void;
} & ThemeControlProps) {
  const [tab, setTab] = useState<"users" | "resources">("users"),
    [userView, setUserView] = useState<"active" | "archived">("active"),
    [resourceView, setResourceView] = useState<"active" | "archived">("active"),
    [users, setUsers] = useState<User[]>([]),
    [resources, setResources] = useState<Resource[]>([]),
    [passwordPolicy, setPasswordPolicy] = useState<PasswordPolicy | null>(null),
    [loginAttemptPolicy, setLoginAttemptPolicy] =
      useState<LoginAttemptPolicy | null>(null),
    [policySaving, setPolicySaving] = useState(false),
    [loginAttemptPolicySaving, setLoginAttemptPolicySaving] = useState(false),
    [error, setError] = useState(""),
    [search, setSearch] = useState(""),
    [passwordUser, setPasswordUser] = useState<User | null>(null),
    [archiveUser, setArchiveUser] = useState<User | null>(null),
    [archiveResource, setArchiveResource] = useState<Resource | null>(null);
  const load = () =>
    Promise.all([
      api<User[]>("/admin/users?search=" + encodeURIComponent(search)),
      api<Resource[]>("/admin/resources?search=" + encodeURIComponent(search)),
      api<PasswordPolicy>("/admin/password-policy"),
      api<LoginAttemptPolicy>("/admin/login-attempt-policy"),
    ])
      .then(([u, r, policy, attemptPolicy]) => {
        setUsers(u);
        setResources(r);
        setPasswordPolicy(policy);
        setLoginAttemptPolicy(attemptPolicy);
        setError("");
      })
      .catch((x) => setError(x.message));
  useEffect(() => {
    load();
  }, []);
  const submit = async (
    e: React.FormEvent<HTMLFormElement>,
    kind: "user" | "resource",
  ) => {
    e.preventDefault();
    const form = e.currentTarget;
    const f = new FormData(form);
    try {
      if (kind === "user") {
        const created = await api<User>("/admin/users", {
          method: "POST",
          body: JSON.stringify({
            displayName: f.get("displayName"),
            email: f.get("email"),
            role: f.get("role"),
            temporaryPassword: f.get("password"),
          }),
        });
        setUsers((current) =>
          [...current, created].sort((a, b) =>
            a.displayName.localeCompare(b.displayName, "ja"),
          ),
        );
      } else {
        const created = await api<Resource>("/admin/resources", {
          method: "POST",
          body: JSON.stringify({
            name: f.get("name"),
            kind: f.get("kind"),
            isActive: true,
            version: 0,
          }),
        });
        setResources((current) => [...current, created]);
      }
      form.reset();
      setError("");
    } catch (x) {
      setError((x as Error).message);
    }
  };
  const savePasswordPolicy = async () => {
    if (!passwordPolicy) return;
    setPolicySaving(true);
    try {
      const updated = await api<PasswordPolicy>("/admin/password-policy", {
        method: "PUT",
        body: JSON.stringify(passwordPolicy),
      });
      setPasswordPolicy(updated);
      setError("");
    } catch (x) {
      setError((x as Error).message);
    } finally {
      setPolicySaving(false);
    }
  };
  const saveLoginAttemptPolicy = async () => {
    if (!loginAttemptPolicy) return;
    setLoginAttemptPolicySaving(true);
    try {
      const updated = await api<LoginAttemptPolicy>(
        "/admin/login-attempt-policy",
        {
          method: "PUT",
          body: JSON.stringify(loginAttemptPolicy),
        },
      );
      setLoginAttemptPolicy(updated);
      setError("");
    } catch (x) {
      setError((x as Error).message);
    } finally {
      setLoginAttemptPolicySaving(false);
    }
  };
  const saveUser = async (u: User) => {
    try {
      const updated = await api<User>("/admin/users/" + u.id, {
        method: "PUT",
        body: JSON.stringify(u),
      });
      setUsers((current) =>
        current.map((item) => (item.id === updated.id ? updated : item)),
      );
      setError("");
    } catch (x) {
      setError((x as Error).message);
    }
  };
  const restoreUser = async (user: User) => {
    await saveUser({ ...user, isActive: true });
  };
  const saveResource = async (r: Resource) => {
    try {
      const updated = await api<Resource>("/admin/resources/" + r.id, {
        method: "PUT",
        body: JSON.stringify(r),
      });
      setResources((current) =>
        current.map((item) => (item.id === updated.id ? updated : item)),
      );
      setError("");
    } catch (x) {
      setError((x as Error).message);
    }
  };
  const restoreResource = async (resource: Resource) => {
    await saveResource({ ...resource, isActive: true });
  };
  const displayedUsers = users.filter((user) =>
    userView === "active" ? user.isActive : !user.isActive,
  );
  const displayedResources = resources.filter((resource) =>
    resourceView === "active" ? resource.isActive : !resource.isActive,
  );
  return (
    <div className="adminApp">
      <header className="appBar">
        <div className="brand">
          <img className="appLogo" src="/calendar-logo.svg" alt="kobareo-calendar" />
          <span className="appSection">管理センター</span>
        </div>
        <div className="account">
          <ThemeToggle
            colorMode={colorMode}
            toggleColorMode={toggleColorMode}
          />
          <Avatar name={me.displayName} size={28} />
          <span>{me.displayName}</span>
          <Button
            appearance="subtle"
            icon={<SignOut20Regular />}
            onClick={onLogout}
            aria-label="ログアウト"
          />
        </div>
      </header>
      <main className="admin">
        <div className="adminCommand">
          <Button
            appearance="subtle"
            icon={<ArrowLeft20Regular />}
            onClick={close}
          >
            予定表へ戻る
          </Button>
          <div className="adminTabs" role="tablist" aria-label="管理対象">
            <Button
              role="tab"
              aria-selected={tab === "users"}
              appearance={tab === "users" ? "primary" : "subtle"}
              icon={<Person20Regular />}
              onClick={() => setTab("users")}
            >
              ユーザー <span className="countBadge">{users.length}</span>
            </Button>
            <Button
              role="tab"
              aria-selected={tab === "resources"}
              appearance={tab === "resources" ? "primary" : "subtle"}
              icon={<Building20Regular />}
              onClick={() => setTab("resources")}
            >
              会議室・備品{" "}
              <span className="countBadge">{resources.length}</span>
            </Button>
          </div>
          <form
            className="adminSearch"
            onSubmit={(e) => {
              e.preventDefault();
              load();
            }}
          >
            <Input
              aria-label="管理項目を検索"
              contentBefore={<Search20Regular />}
              placeholder="名前・メールを検索"
              value={search}
              onChange={(_, data) => setSearch(data.value)}
            />
            <Button type="submit" appearance="secondary">
              検索
            </Button>
          </form>
        </div>
        <div className="adminHeading">
          <div>
            <h1>{tab === "users" ? "ユーザー" : "会議室・備品"}</h1>
            <p>
              {tab === "users"
                ? "アクセス権、ロール、アカウント状態を管理します。"
                : "予定に割り当てる会議室と備品を管理します。"}
            </p>
          </div>
        </div>
        {error && (
          <div role="alert" className="alert">
            {error}
          </div>
        )}
        {tab === "users" ? (
          <>
            <div
              className="userArchiveTabs"
              role="tablist"
              aria-label="ユーザー状態"
            >
              <Button
                role="tab"
                aria-selected={userView === "active"}
                appearance={userView === "active" ? "primary" : "subtle"}
                onClick={() => setUserView("active")}
              >
                利用中
                <span className="countBadge">
                  {users.filter((user) => user.isActive).length}
                </span>
              </Button>
              <Button
                role="tab"
                aria-selected={userView === "archived"}
                appearance={userView === "archived" ? "primary" : "subtle"}
                icon={<Archive20Regular />}
                onClick={() => setUserView("archived")}
              >
                アーカイブ
                <span className="countBadge">
                  {users.filter((user) => !user.isActive).length}
                </span>
              </Button>
            </div>
            {passwordPolicy && (
              <section className="passwordPolicy" aria-labelledby="password-policy-title">
                <div className="formTitle">
                  <Key20Regular />
                  <span id="password-policy-title">パスワードポリシー</span>
                </div>
                <label>
                  最小文字数
                  <input
                    type="number"
                    min="8"
                    max="128"
                    value={passwordPolicy.requiredLength}
                    onChange={(event) =>
                      setPasswordPolicy((current) =>
                        current
                          ? {
                              ...current,
                              requiredLength: Number(event.target.value),
                            }
                          : current,
                      )
                    }
                  />
                </label>
                {(
                  [
                    ["requireDigit", "数字を必須にする"],
                    ["requireLowercase", "小文字を必須にする"],
                    ["requireUppercase", "大文字を必須にする"],
                    ["requireNonAlphanumeric", "記号を必須にする"],
                  ] as const
                ).map(([key, label]) => (
                  <label className="policyCheck" key={key}>
                    <input
                      type="checkbox"
                      checked={passwordPolicy[key]}
                      onChange={(event) =>
                        setPasswordPolicy((current) =>
                          current
                            ? { ...current, [key]: event.target.checked }
                            : current,
                        )
                      }
                    />
                    {label}
                  </label>
                ))}
                <Button
                  appearance="secondary"
                  icon={<Save20Regular />}
                  disabled={policySaving}
                  onClick={savePasswordPolicy}
                >
                  {policySaving ? "保存中…" : "ポリシーを保存"}
                </Button>
              </section>
            )}
            {loginAttemptPolicy && (
              <section
                className="passwordPolicy loginAttemptPolicy"
                aria-labelledby="login-attempt-policy-title"
              >
                <div className="formTitle">
                  <LockClosed20Regular />
                  <span id="login-attempt-policy-title">ログイン試行回数</span>
                </div>
                <label className="policyCheck">
                  <input
                    type="checkbox"
                    checked={loginAttemptPolicy.isEnabled}
                    onChange={(event) =>
                      setLoginAttemptPolicy((current) =>
                        current
                          ? { ...current, isEnabled: event.target.checked }
                          : current,
                      )
                    }
                  />
                  制限を有効にする
                </label>
                <label>
                  試行回数
                  <input
                    type="number"
                    min="1"
                    max="100"
                    disabled={!loginAttemptPolicy.isEnabled}
                    value={loginAttemptPolicy.maxFailedAttempts}
                    onChange={(event) =>
                      setLoginAttemptPolicy((current) =>
                        current
                          ? {
                              ...current,
                              maxFailedAttempts: Number(event.target.value),
                            }
                          : current,
                      )
                    }
                  />
                </label>
                <span className="policyDescription">
                  有効時は指定回数の失敗で15分間ロックします。IP単位のレート制限は別途維持されます。
                </span>
                <Button
                  appearance="secondary"
                  icon={<Save20Regular />}
                  disabled={loginAttemptPolicySaving}
                  onClick={saveLoginAttemptPolicy}
                >
                  {loginAttemptPolicySaving ? "保存中…" : "設定を保存"}
                </Button>
              </section>
            )}
            {userView === "active" && (
              <form className="addForm" onSubmit={(e) => submit(e, "user")}>
                <div className="formTitle">
                  <Person20Regular />
                  <span>ユーザーを追加</span>
                </div>
                <input
                  name="displayName"
                  aria-label="表示名"
                  placeholder="表示名"
                  required
                />
                <input
                  name="email"
                  aria-label="メールアドレス"
                  type="email"
                  placeholder="メールアドレス"
                  required
                />
                <select name="role" aria-label="ロール">
                  <option>User</option>
                  <option>Admin</option>
                </select>
                <input
                  name="password"
                  aria-label="仮パスワード"
                  type="password"
                  placeholder={`仮パスワード（${passwordPolicy?.requiredLength ?? 12}文字以上）`}
                  required
                />
                <Button
                  type="submit"
                  appearance="primary"
                  icon={<Add20Regular />}
                >
                  追加
                </Button>
              </form>
            )}
            <div className="cardHeader userColumns" aria-hidden="true">
              <span>ユーザー</span>
              <span>メールアドレス</span>
              <span>ロール</span>
              <span>状態</span>
              <span>操作</span>
            </div>
            <div className="cards userCards">
              {displayedUsers.map((u) => (
                <div className="card userCard" key={u.id}>
                  <div className="identityCell">
                    <Avatar name={u.displayName} size={32} />
                    <input
                      aria-label="表示名"
                      value={u.displayName}
                      disabled={!u.isActive}
                      onChange={(e) =>
                        setUsers((x) =>
                          x.map((v) =>
                            v.id === u.id
                              ? { ...v, displayName: e.target.value }
                              : v,
                          ),
                        )
                      }
                    />
                  </div>
                  <input
                    aria-label="メールアドレス"
                    type="email"
                    value={u.email}
                    disabled={!u.isActive}
                    onChange={(e) =>
                      setUsers((x) =>
                        x.map((v) =>
                          v.id === u.id ? { ...v, email: e.target.value } : v,
                        ),
                      )
                    }
                  />
                  <select
                    aria-label="ロール"
                    value={u.role}
                    disabled={!u.isActive}
                    onChange={(e) =>
                      setUsers((x) =>
                        x.map((v) =>
                          v.id === u.id
                            ? { ...v, role: e.target.value as User["role"] }
                            : v,
                        ),
                      )
                    }
                  >
                    <option>User</option>
                    <option>Admin</option>
                  </select>
                  <span
                    className={`statusToggle ${u.isActive ? "enabled" : "disabled"}`}
                  >
                    {u.isActive ? "利用中" : "アーカイブ済み"}
                  </span>
                  <div className="cardActions">
                    {u.isActive ? (
                      <>
                        <Button
                          appearance="subtle"
                          icon={<Save20Regular />}
                          onClick={() => saveUser(u)}
                          aria-label={`${u.displayName}を保存`}
                        />
                        <Button
                          appearance="subtle"
                          icon={<Key20Regular />}
                          onClick={() => setPasswordUser(u)}
                          aria-label={`${u.displayName}の仮パスワードを設定`}
                        />
                        <Button
                          appearance="subtle"
                          icon={<Archive20Regular />}
                          disabled={u.id === me.id}
                          onClick={() => setArchiveUser(u)}
                          aria-label={`${u.displayName}をアーカイブ`}
                        />
                      </>
                    ) : (
                      <Button
                        appearance="subtle"
                        icon={<ArrowUndo20Regular />}
                        onClick={() => restoreUser(u)}
                        aria-label={`${u.displayName}を利用中に戻す`}
                      />
                    )}
                  </div>
                </div>
              ))}
            </div>
          </>
        ) : (
          <>
            <div
              className="userArchiveTabs"
              role="tablist"
              aria-label="会議室・備品の状態"
            >
              <Button
                role="tab"
                aria-selected={resourceView === "active"}
                appearance={resourceView === "active" ? "primary" : "subtle"}
                onClick={() => setResourceView("active")}
              >
                利用中
                <span className="countBadge">
                  {resources.filter((resource) => resource.isActive).length}
                </span>
              </Button>
              <Button
                role="tab"
                aria-selected={resourceView === "archived"}
                appearance={
                  resourceView === "archived" ? "primary" : "subtle"
                }
                icon={<Archive20Regular />}
                onClick={() => setResourceView("archived")}
              >
                アーカイブ
                <span className="countBadge">
                  {resources.filter((resource) => !resource.isActive).length}
                </span>
              </Button>
            </div>
            {resourceView === "active" && (
              <form
                className="addForm resourceAdd"
                onSubmit={(e) => submit(e, "resource")}
              >
                <div className="formTitle">
                  <Building20Regular />
                  <span>リソースを追加</span>
                </div>
                <input
                  name="name"
                  aria-label="名称"
                  placeholder="会議室・備品の名称"
                  required
                />
                <select name="kind" aria-label="種類">
                  <option value="Room">会議室</option>
                  <option value="Equipment">備品</option>
                </select>
                <Button
                  type="submit"
                  appearance="primary"
                  icon={<Add20Regular />}
                >
                  追加
                </Button>
              </form>
            )}
            <div className="cardHeader resourceColumns" aria-hidden="true">
              <span>名称</span>
              <span>種類</span>
              <span>状態</span>
              <span>操作</span>
            </div>
            <div className="cards resourceCards">
              {displayedResources.map((r) => (
                <div className="card resourceCard" key={r.id}>
                  <div className="identityCell">
                    <Avatar icon={<Building20Regular />} size={32} />
                    <input
                      aria-label="名称"
                      value={r.name}
                      disabled={!r.isActive}
                      onChange={(e) =>
                        setResources((x) =>
                          x.map((v) =>
                            v.id === r.id ? { ...v, name: e.target.value } : v,
                          ),
                        )
                      }
                    />
                  </div>
                  <select
                    aria-label="種類"
                    value={r.kind}
                    disabled={!r.isActive}
                    onChange={(e) =>
                      setResources((x) =>
                        x.map((v) =>
                          v.id === r.id
                            ? { ...v, kind: e.target.value as Resource["kind"] }
                            : v,
                        ),
                      )
                    }
                  >
                    <option value="Room">会議室</option>
                    <option value="Equipment">備品</option>
                  </select>
                  <span
                    className={`statusToggle ${r.isActive ? "enabled" : "disabled"}`}
                  >
                    {r.isActive ? "利用中" : "アーカイブ済み"}
                  </span>
                  <div className="cardActions">
                    {r.isActive ? (
                      <>
                        <Button
                          appearance="subtle"
                          icon={<Save20Regular />}
                          onClick={() => saveResource(r)}
                          aria-label={`${r.name}を保存`}
                        />
                        <Button
                          appearance="subtle"
                          icon={<Archive20Regular />}
                          onClick={() => setArchiveResource(r)}
                          aria-label={`${r.name}をアーカイブ`}
                        />
                      </>
                    ) : (
                      <Button
                        appearance="subtle"
                        icon={<ArrowUndo20Regular />}
                        onClick={() => restoreResource(r)}
                        aria-label={`${r.name}を利用中に戻す`}
                      />
                    )}
                  </div>
                </div>
              ))}
            </div>
          </>
        )}
      </main>
      <PasswordResetDialog
        user={passwordUser}
        onClose={() => setPasswordUser(null)}
      />
      <ArchiveUserDialog
        user={archiveUser}
        onClose={() => setArchiveUser(null)}
        onArchived={(updated) => {
          setUsers((current) =>
            current.map((item) => (item.id === updated.id ? updated : item)),
          );
          setArchiveUser(null);
        }}
      />
      <ArchiveResourceDialog
        resource={archiveResource}
        onClose={() => setArchiveResource(null)}
        onArchived={(updated) => {
          setResources((current) =>
            current.map((item) => (item.id === updated.id ? updated : item)),
          );
          setArchiveResource(null);
        }}
      />
    </div>
  );
}

function ArchiveResourceDialog({
  resource,
  onClose,
  onArchived,
}: {
  resource: Resource | null;
  onClose: () => void;
  onArchived: (resource: Resource) => void;
}) {
  const [saving, setSaving] = useState(false),
    [error, setError] = useState("");
  useEffect(() => {
    setSaving(false);
    setError("");
  }, [resource?.id]);
  const archive = async () => {
    if (!resource) return;
    setSaving(true);
    setError("");
    try {
      const updated = await api<Resource>(`/admin/resources/${resource.id}`, {
        method: "PUT",
        body: JSON.stringify({ ...resource, isActive: false }),
      });
      onArchived(updated);
    } catch (reason) {
      setError((reason as Error).message);
      setSaving(false);
    }
  };
  return (
    <Dialog
      open={resource !== null}
      modalType="alert"
      onOpenChange={(_, data) => {
        if (!data.open && !saving) onClose();
      }}
    >
      <DialogSurface className="archiveDialog">
        <DialogBody>
          <DialogTitle>
            <span className="archiveTitle">
              <Archive20Regular />
              会議室・備品をアーカイブ
            </span>
          </DialogTitle>
          <DialogContent className="archiveDialogContent">
            <p>
              <strong>{resource?.name}</strong>をアーカイブしますか？
            </p>
            <ul>
              <li>新しい予定の予約候補には表示されません。</li>
              <li>過去の予定と名称は履歴として保持されます。</li>
              <li>既存の予定に含まれる予約情報は削除されません。</li>
              <li>管理画面からいつでも利用中に戻せます。</li>
            </ul>
            {error && (
              <div className="alert" role="alert">
                {error}
              </div>
            )}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose} disabled={saving}>
              キャンセル
            </Button>
            <Button appearance="primary" onClick={archive} disabled={saving}>
              {saving ? "アーカイブ中…" : "アーカイブする"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function ArchiveUserDialog({
  user,
  onClose,
  onArchived,
}: {
  user: User | null;
  onClose: () => void;
  onArchived: (user: User) => void;
}) {
  const [saving, setSaving] = useState(false),
    [error, setError] = useState("");
  useEffect(() => {
    setSaving(false);
    setError("");
  }, [user?.id]);
  const archive = async () => {
    if (!user) return;
    setSaving(true);
    setError("");
    try {
      const updated = await api<User>(`/admin/users/${user.id}`, {
        method: "PUT",
        body: JSON.stringify({ ...user, isActive: false }),
      });
      onArchived(updated);
    } catch (reason) {
      setError((reason as Error).message);
      setSaving(false);
    }
  };
  return (
    <Dialog
      open={user !== null}
      modalType="alert"
      onOpenChange={(_, data) => {
        if (!data.open && !saving) onClose();
      }}
    >
      <DialogSurface className="archiveDialog">
        <DialogBody>
          <DialogTitle>
            <span className="archiveTitle">
              <Archive20Regular />
              ユーザーをアーカイブ
            </span>
          </DialogTitle>
          <DialogContent className="archiveDialogContent">
            <p>
              <strong>{user?.displayName}</strong>をアーカイブしますか？
            </p>
            <ul>
              <li>このユーザーはログインできなくなります。</li>
              <li>新しい予定の対象ユーザーには表示されません。</li>
              <li>過去の予定とユーザー名は履歴として保持されます。</li>
              <li>管理画面からいつでも利用中に戻せます。</li>
            </ul>
            {error && (
              <div className="alert" role="alert">
                {error}
              </div>
            )}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose} disabled={saving}>
              キャンセル
            </Button>
            <Button appearance="primary" onClick={archive} disabled={saving}>
              {saving ? "アーカイブ中…" : "アーカイブする"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function PasswordResetDialog({
  user,
  onClose,
}: {
  user: User | null;
  onClose: () => void;
}) {
  const [password, setPassword] = useState(""),
    [confirmation, setConfirmation] = useState(""),
    [error, setError] = useState(""),
    [saving, setSaving] = useState(false),
    [completed, setCompleted] = useState(false);
  useEffect(() => {
    setPassword("");
    setConfirmation("");
    setError("");
    setSaving(false);
    setCompleted(false);
  }, [user?.id]);
  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (
      password.length < 12 ||
      !/[a-z]/.test(password) ||
      !/[A-Z]/.test(password) ||
      !/[0-9]/.test(password) ||
      !/[^A-Za-z0-9]/.test(password)
    ) {
      setError("12文字以上で、英大文字・英小文字・数字・記号を含めてください。");
      return;
    }
    if (password !== confirmation) {
      setError("確認用パスワードが一致しません。");
      return;
    }
    if (!user) return;
    setSaving(true);
    setError("");
    try {
      await api(`/admin/users/${user.id}/temporary-password`, {
        method: "POST",
        body: JSON.stringify({ temporaryPassword: password }),
      });
      setCompleted(true);
      setPassword("");
      setConfirmation("");
    } catch (reason) {
      setError((reason as Error).message);
    } finally {
      setSaving(false);
    }
  };
  return (
    <Dialog
      open={user !== null}
      modalType="modal"
      onOpenChange={(_, data) => {
        if (!data.open && !saving) onClose();
      }}
    >
      <DialogSurface className="passwordDialog">
        <DialogBody>
          <DialogTitle>仮パスワードを設定</DialogTitle>
          {completed ? (
            <>
              <DialogContent>
                <p className="passwordSuccess">
                  {user?.displayName}の仮パスワードを設定しました。
                </p>
              </DialogContent>
              <DialogActions>
                <Button appearance="primary" onClick={onClose}>
                  閉じる
                </Button>
              </DialogActions>
            </>
          ) : (
            <form onSubmit={submit}>
              <DialogContent className="passwordDialogContent">
                <p>
                  <strong>{user?.displayName}</strong>が次回ログインに使用する
                  仮パスワードを入力してください。
                </p>
                <label>
                  新しい仮パスワード
                  <Input
                    type="password"
                    value={password}
                    onChange={(_, data) => setPassword(data.value)}
                    autoComplete="new-password"
                    autoFocus
                    required
                  />
                </label>
                <label>
                  仮パスワードを再入力
                  <Input
                    type="password"
                    value={confirmation}
                    onChange={(_, data) => setConfirmation(data.value)}
                    autoComplete="new-password"
                    required
                  />
                </label>
                <small>
                  12文字以上で、英大文字・英小文字・数字・記号を含めてください。
                </small>
                {error && (
                  <div className="alert" role="alert">
                    {error}
                  </div>
                )}
              </DialogContent>
              <DialogActions>
                <Button
                  type="button"
                  appearance="secondary"
                  onClick={onClose}
                  disabled={saving}
                >
                  キャンセル
                </Button>
                <Button type="submit" appearance="primary" disabled={saving}>
                  {saving ? "設定中…" : "設定する"}
                </Button>
              </DialogActions>
            </form>
          )}
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function Root() {
  const [colorMode, setColorMode] = useState<ColorMode>(() =>
    localStorage.getItem(themeStorageKey) === "dark" ? "dark" : "light",
  );
  useEffect(() => {
    document.documentElement.dataset.theme = colorMode;
    document.documentElement.style.colorScheme = colorMode;
    localStorage.setItem(themeStorageKey, colorMode);
  }, [colorMode]);
  return (
    <FluentProvider
      theme={colorMode === "dark" ? kobareoDarkTheme : kobareoLightTheme}
    >
      <App
        colorMode={colorMode}
        toggleColorMode={() =>
          setColorMode((current) => (current === "dark" ? "light" : "dark"))
        }
      />
    </FluentProvider>
  );
}

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>,
);
