using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace KobareoCalendar.WebApp.Tests;

public sealed class ApiSecurityFlowTests : IClassFixture<KobareoCalendarFactory>
{
    private readonly KobareoCalendarFactory factory;
    public ApiSecurityFlowTests(KobareoCalendarFactory factory) => this.factory = factory;

    [Fact]
    public async Task Authorization_privacy_conflict_and_archived_targets_are_enforced()
    {
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Login(admin, "admin@example.com", "Integration123!");
        var user = await CreateUser(admin, "利用者", "user@example.com");
        var outsider = await CreateUser(admin, "第三者", "outsider@example.com");
        var resource = await Post(admin, "/api/admin/resources", new { name = "統合テスト会議室", kind = "Room", isActive = true, version = 0L });
        var resourceId = resource.RootElement.GetProperty("id").GetGuid();

        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Login(owner, "user@example.com", "Temporary123!");
        await Login(other, "outsider@example.com", "Temporary123!");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/admin/check")).StatusCode);

        var first = await Post(owner, "/api/schedules", ScheduleBody(user, resourceId, "秘密の会議", false, 9, 11, true));
        var scheduleId = first.RootElement.GetProperty("id").GetGuid();
        var listed = await other.GetFromJsonAsync<JsonElement>($"/api/schedules?from=2026-08-15T00:00:00Z&to=2026-08-16T00:00:00Z&targets={resourceId}");
        var redacted = listed.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == scheduleId);
        Assert.Equal("非公開の予定", redacted.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, redacted.GetProperty("details").ValueKind);

        var update = await Send(other, HttpMethod.Put, $"/api/schedules/{scheduleId}", ScheduleBody(outsider, resourceId, "改ざん", false, 9, 11, false, 0));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        var delete = await Send(other, HttpMethod.Delete, $"/api/schedules/{scheduleId}?version=0", null);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        var adminUpdate = await Send(admin, HttpMethod.Put, $"/api/schedules/{scheduleId}", ScheduleBody(user, resourceId, "管理者による改ざん", false, 9, 11, false, 0));
        Assert.Equal(HttpStatusCode.Forbidden, adminUpdate.StatusCode);
        var adminDelete = await Send(admin, HttpMethod.Delete, $"/api/schedules/{scheduleId}?version=0", null);
        Assert.Equal(HttpStatusCode.Forbidden, adminDelete.StatusCode);

        var conflict = await Send(owner, HttpMethod.Post, "/api/schedules", ScheduleBody(user, resourceId, "重複", false, 10, 12, false));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var confirmed = await Post(owner, "/api/schedules", ScheduleBody(user, resourceId, "重複", true, 10, 12, false));
        Assert.Equal("重複", confirmed.RootElement.GetProperty("title").GetString());

        var historical = await Post(owner, "/api/schedules", ScheduleBody(outsider, Guid.Empty, "履歴保持", false, 15, 16, false));
        var historicalId = historical.RootElement.GetProperty("id").GetGuid();
        var notificationList = await other.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(1, notificationList.GetProperty("unreadCount").GetInt32());
        var notification = notificationList.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("利用者", notification.GetProperty("actorDisplayName").GetString());
        Assert.Equal("履歴保持", notification.GetProperty("scheduleTitle").GetString());
        var readNotification = await Send(other, HttpMethod.Post, "/api/notifications/read", new { ids = new[] { notification.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.NoContent, readNotification.StatusCode);
        notificationList = await other.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(0, notificationList.GetProperty("unreadCount").GetInt32());
        await Put(admin, $"/api/admin/users/{outsider}", new { displayName = "第三者", email = "outsider@example.com", role = "User", isActive = false });
        using var archivedLogin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(archivedLogin, HttpMethod.Post, "/api/auth/login", new { email = "outsider@example.com", password = "Temporary123!" })).StatusCode);
        var targets = await owner.GetFromJsonAsync<JsonElement>("/api/targets");
        var archivedTarget = targets.GetProperty("users").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == outsider);
        Assert.False(archivedTarget.GetProperty("isActive").GetBoolean());
        var historicalPreference = await Send(owner, HttpMethod.Put, "/api/timeline-preferences", new { targets = new[] { new { kind = "User", id = outsider, isVisible = false } } });
        Assert.Equal(HttpStatusCode.OK, historicalPreference.StatusCode);
        var savedPreference = await owner.GetFromJsonAsync<JsonElement>("/api/timeline-preferences");
        Assert.False(savedPreference.GetProperty("targets").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == outsider).GetProperty("isVisible").GetBoolean());
        var historicalUpdate = await Send(owner, HttpMethod.Put, $"/api/schedules/{historicalId}", ScheduleBody(outsider, Guid.Empty, "履歴を保持して更新", false, 15, 16, false, 0));
        Assert.Equal(HttpStatusCode.OK, historicalUpdate.StatusCode);
        var inactiveUser = await Send(owner, HttpMethod.Post, "/api/schedules", ScheduleBody(outsider, Guid.Empty, "無効利用者", false, 13, 14, false));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveUser.StatusCode);
        await Put(admin, $"/api/admin/resources/{resourceId}", new { name = "統合テスト会議室", kind = "Room", isActive = false, version = 0L });
        targets = await owner.GetFromJsonAsync<JsonElement>("/api/targets");
        var archivedResource = targets.GetProperty("resources").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == resourceId);
        Assert.False(archivedResource.GetProperty("isActive").GetBoolean());
        historicalPreference = await Send(owner, HttpMethod.Put, "/api/timeline-preferences", new { targets = new[] { new { kind = "Resource", id = resourceId } } });
        Assert.Equal(HttpStatusCode.OK, historicalPreference.StatusCode);
        var archivedResourceUpdate = await Send(owner, HttpMethod.Put, $"/api/schedules/{scheduleId}", ScheduleBody(user, resourceId, "アーカイブ済み会議室の履歴を保持", true, 9, 11, true, 0));
        Assert.Equal(HttpStatusCode.OK, archivedResourceUpdate.StatusCode);
        var inactiveResource = await Send(owner, HttpMethod.Post, "/api/schedules", ScheduleBody(user, resourceId, "無効設備", false, 13, 14, false));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveResource.StatusCode);
    }

    [Fact]
    public async Task Anonymous_mutation_requires_csrf_and_security_headers_are_present()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.com", password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var health = await client.GetAsync("/api/health");
        Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", health.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Authenticated_websocket_receives_schedule_change_notification()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        var csrfCookie = CookiePair(csrfResponse.Headers.GetValues("Set-Cookie").Single());
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email = "admin@example.com", password = "Integration123!" }) };
        login.Headers.Add("Cookie", csrfCookie); login.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var loginResponse = await client.SendAsync(login); loginResponse.EnsureSuccessStatusCode();
        var sessionCookie = CookiePair(loginResponse.Headers.GetValues("Set-Cookie").Single(x => x.Contains("session")));
        var authenticatedCsrfRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf"); authenticatedCsrfRequest.Headers.Add("Cookie", sessionCookie);
        var authenticatedCsrfResponse = await client.SendAsync(authenticatedCsrfRequest);
        csrf = await authenticatedCsrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        csrfCookie = CookiePair(authenticatedCsrfResponse.Headers.GetValues("Set-Cookie").Single());
        var cookies = $"{csrfCookie}; {sessionCookie}";

        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"); meRequest.Headers.Add("Cookie", cookies);
        var me = await (await client.SendAsync(meRequest)).Content.ReadFromJsonAsync<JsonElement>();
        var socketClient = factory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers["Cookie"] = cookies;
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/api/realtime"), CancellationToken.None);

        var create = new HttpRequestMessage(HttpMethod.Post, "/api/schedules")
        {
            Content = JsonContent.Create(new { title = "リアルタイム通知テスト", details = "", startsAtUtc = "2026-09-01T01:00:00Z", endsAtUtc = "2026-09-01T01:05:00Z", isPrivate = false, participantIds = new[] { me.GetProperty("id").GetGuid() }, resourceIds = Array.Empty<Guid>(), confirmConflicts = true })
        };
        create.Headers.Add("Cookie", cookies); create.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var created = await client.SendAsync(create); created.EnsureSuccessStatusCode();
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();

        var buffer = new byte[512]; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await socket.ReceiveAsync(buffer, timeout.Token);
        var message = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
        Assert.Equal("schedule.changed", message.RootElement.GetProperty("type").GetString());
        Assert.Equal(createdBody.GetProperty("id").GetGuid(), message.RootElement.GetProperty("scheduleId").GetGuid());
        Assert.Equal("created", message.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public async Task Admin_can_update_password_policy_and_validation_errors_are_japanese()
    {
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Login(admin, "admin@example.com", "Integration123!");

        var original = await admin.GetFromJsonAsync<JsonElement>("/api/admin/password-policy");
        try
        {
            await Put(
                admin,
                "/api/admin/password-policy",
                new
                {
                    requiredLength = 12,
                    requireDigit = true,
                    requireLowercase = true,
                    requireUppercase = true,
                    requireNonAlphanumeric = true,
                }
            );
            var rejected = await Send(
                admin,
                HttpMethod.Post,
                "/api/admin/users",
                new
                {
                    displayName = "弱いパスワード",
                    email = $"weak-{Guid.NewGuid():N}@example.com",
                    role = "User",
                    temporaryPassword = "lowercaseonly",
                }
            );
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("数字を1文字以上", error.GetProperty("detail").GetString());
            Assert.Contains("大文字を1文字以上", error.GetProperty("detail").GetString());
            Assert.Contains("記号を1文字以上", error.GetProperty("detail").GetString());

            await Put(
                admin,
                "/api/admin/password-policy",
                new
                {
                    requiredLength = 8,
                    requireDigit = false,
                    requireLowercase = false,
                    requireUppercase = false,
                    requireNonAlphanumeric = false,
                }
            );
            var created = await Send(
                admin,
                HttpMethod.Post,
                "/api/admin/users",
                new
                {
                    displayName = "緩和ポリシー",
                    email = $"relaxed-{Guid.NewGuid():N}@example.com",
                    role = "User",
                    temporaryPassword = "abcdefgh",
                }
            );
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        finally
        {
            await Put(admin, "/api/admin/password-policy", original);
        }
    }

    [Fact]
    public async Task Admin_can_enable_and_disable_login_attempt_lockout()
    {
        using var admin = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await Login(admin, "admin@example.com", "Integration123!");
        var original = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/login-attempt-policy"
        );
        var email = $"lockout-{Guid.NewGuid():N}@example.com";
        await Post(
            admin,
            "/api/admin/users",
            new
            {
                displayName = "ロック試験",
                email,
                role = "User",
                temporaryPassword = "Temporary123!",
            }
        );

        try
        {
            await Put(
                admin,
                "/api/admin/login-attempt-policy",
                new { isEnabled = true, maxFailedAttempts = 2 }
            );
            using var user = factory.CreateClient(
                new WebApplicationFactoryClientOptions { HandleCookies = true }
            );
            var firstFailure = await Send(
                user,
                HttpMethod.Post,
                "/api/auth/login",
                new { email, password = "WrongPassword1!" }
            );
            Assert.Equal(HttpStatusCode.Unauthorized, firstFailure.StatusCode);
            var secondFailure = await Send(
                user,
                HttpMethod.Post,
                "/api/auth/login",
                new { email, password = "WrongPassword2!" }
            );
            Assert.Equal(HttpStatusCode.Unauthorized, secondFailure.StatusCode);
            Assert.Contains(
                "一時的にロック",
                (await secondFailure.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("detail")
                    .GetString()
            );

            await Put(
                admin,
                "/api/admin/login-attempt-policy",
                new { isEnabled = false, maxFailedAttempts = 2 }
            );
            await Login(user, email, "Temporary123!");
        }
        finally
        {
            await Put(admin, "/api/admin/login-attempt-policy", original);
        }
    }

    private static string CookiePair(string setCookie) => setCookie.Split(';', 2)[0];

    private static object ScheduleBody(Guid userId, Guid resourceId, string title, bool confirmed, int startHour, int endHour, bool isPrivate, long version = 0) => new
    {
        title,
        details = "APIに漏れてはいけない詳細",
        startsAtUtc = $"2026-08-15T{startHour:00}:00:00Z",
        endsAtUtc = $"2026-08-15T{endHour:00}:00:00Z",
        isPrivate,
        participantIds = new[] { userId },
        resourceIds = resourceId == Guid.Empty ? Array.Empty<Guid>() : new[] { resourceId },
        confirmConflicts = confirmed,
        version
    };
    private static async Task<Guid> CreateUser(HttpClient client, string name, string email)
    {
        var json = await Post(client, "/api/admin/users", new { displayName = name, email, role = "User", temporaryPassword = "Temporary123!" });
        return json.RootElement.GetProperty("id").GetGuid();
    }
    private static async Task Login(HttpClient client, string email, string password)
    {
        var response = await Send(client, HttpMethod.Post, "/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
    }
    private static async Task<JsonDocument> Post(HttpClient client, string path, object body)
    {
        var response = await Send(client, HttpMethod.Post, path, body); response.EnsureSuccessStatusCode(); return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
    private static async Task Put(HttpClient client, string path, object body)
    {
        var response = await Send(client, HttpMethod.Put, path, body); response.EnsureSuccessStatusCode();
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var request = new HttpRequestMessage(method, path); request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}

public sealed class KobareoCalendarFactory : WebApplicationFactory<Program>
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "kobareo-calendar-integration-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(root);
        builder.UseEnvironment("Development");
        builder.UseSetting("DATABASE_PROVIDER", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(root, "test.db")}");
        builder.UseSetting("DATA_PROTECTION_PATH", Path.Combine(root, "keys"));
        builder.UseSetting("COOKIE_SECURE", "false");
        builder.UseSetting("LOGIN_RATE_LIMIT_PER_MINUTE", "100");
        builder.UseSetting("SEED_ADMIN_EMAIL", "admin@example.com");
        builder.UseSetting("SEED_ADMIN_PASSWORD", "Integration123!");
    }
}
