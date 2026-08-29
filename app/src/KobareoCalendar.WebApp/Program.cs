using System.Data;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using KobareoCalendar.Application;
using KobareoCalendar.Domain;
using KobareoCalendar.Infrastructure;
using KobareoCalendar.WebApp;

var builder = WebApplication.CreateBuilder(args);
if (int.TryParse(builder.Configuration["PORT"], out var port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.ConfigureDatabase(builder.Configuration)
);
builder
    .Services.AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(builder.Configuration["DATA_PROTECTION_PATH"] ?? "/data/keys")
    )
    .SetApplicationName("KobareoCalendar");
builder.Services.AddAntiforgery(options =>
{
    var secureCookie = builder.Configuration.GetValue("COOKIE_SECURE", true);
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = secureCookie ? "__Host-kobareo-calendar-csrf" : "kobareo-calendar-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = secureCookie
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.None;
});
builder
    .Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // The effective policy is loaded by DatabasePasswordValidator.
        options.Password.RequiredLength = 1;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        // LoginAttemptPolicyEntity applies the effective account lockout threshold.
        options.Lockout.MaxFailedAccessAttempts = int.MaxValue;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddSignInManager()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddPasswordValidator<DatabasePasswordValidator>()
    .AddDefaultTokenProviders();
builder
    .Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(
        IdentityConstants.ApplicationScheme,
        options =>
        {
            var secureCookie = builder.Configuration.GetValue("COOKIE_SECURE", true);
            options.Cookie.Name = secureCookie ? "__Host-kobareo-calendar-session" : "kobareo-calendar-session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secureCookie
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.None;
            options.Events.OnRedirectToLogin = c =>
            {
                c.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = c =>
            {
                c.Response.StatusCode = 403;
                return Task.CompletedTask;
            };
        }
    );
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (builder.Configuration.GetValue("TRUST_FORWARD_HEADERS", false))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});
builder.Services.AddRateLimiter(options =>
{
    var loginPermitLimit = builder.Configuration.GetValue("LOGIN_RATE_LIMIT_PER_MINUTE", 10);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "login",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);
builder.Services.AddSingleton<ScheduleRealtimeHub>();
builder.Services.AddScoped<ILoginService, IdentityLoginService>();
builder.Services.AddScoped<ILoginAttemptPolicyService, LoginAttemptPolicyService>();

var app = builder.Build();
var runMigrationsOnStartup = builder.Configuration.GetValue<bool?>("RUN_MIGRATIONS_ON_STARTUP")
    ?? !app.Environment.IsProduction();
if (runMigrationsOnStartup)
    await app.Services.MigrateAndSeedIdentityAsync(builder.Configuration);
app.UseExceptionHandler();
app.UseForwardedHeaders();
app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
            + "img-src 'self' data:; connect-src 'self' ws: wss:; object-src 'none'; "
            + "base-uri 'self'; frame-ancestors 'none'";
        await next();
    }
);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.Use(
    async (context, next) =>
    {
        if (
            context.Request.Path.StartsWithSegments("/api")
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method)
        )
        {
            try
            {
                await context
                    .RequestServices.GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new { detail = "CSRFトークンが無効です。" }
                );
                return;
            }
        }
        await next();
    }
);

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
api.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
api.MapGet(
        "/auth/csrf",
        (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })
    )
    .AllowAnonymous();
api.MapPost(
        "/auth/login",
        async (
            LoginRequest request,
            ILoginService loginService,
            CancellationToken cancellationToken
        ) =>
        {
            var result = await loginService.LoginAsync(
                request.Email,
                request.Password,
                cancellationToken
            );
            return result.Status switch
            {
                LoginStatus.Succeeded => Results.Ok(result.User),
                LoginStatus.LockedOut => Results.Problem(
                    "ログイン試行回数を超えたため一時的にロックされています。",
                    statusCode: 401
                ),
                LoginStatus.InvalidCredentials => Results.Problem(
                    "メールアドレスまたはパスワードが正しくありません。",
                    statusCode: 401
                ),
                _ => Results.Problem("ログイン処理に失敗しました。", statusCode: 500),
            };
        }
    )
    .AllowAnonymous()
    .RequireRateLimiting("login");
api.MapPost(
        "/auth/logout",
        async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }
    )
    .RequireAuthorization();
api.MapGet(
        "/auth/me",
        async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await UserDto.FromAsync(user, users));
        }
    )
    .RequireAuthorization();
api.MapGet(
        "/realtime",
        async (HttpContext context, ScheduleRealtimeHub hub) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
                return Results.BadRequest(new { detail = "WebSocket接続が必要です。" });
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.ListenAsync(socket, context.RequestAborted);
            return Results.Empty;
        }
    )
    .RequireAuthorization();
api.MapGet("/admin/check", () => Results.Ok(new { authorized = true }))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));
var adminApi = api.MapGroup("/admin").RequireAuthorization(policy => policy.RequireRole("Admin"));
adminApi.MapGet(
    "/users",
    async (string? search, AppDbContext db, UserManager<ApplicationUser> users) =>
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.DisplayName.Contains(term) || (x.Email != null && x.Email.Contains(term))
            );
        }
        var rows = await query.OrderBy(x => x.DisplayName).Take(200).ToListAsync();
        var result = new List<UserDto>();
        foreach (var user in rows)
            result.Add(await UserDto.FromAsync(user, users));
        return Results.Ok(result);
    }
);
adminApi.MapGet(
    "/password-policy",
    async (AppDbContext db) =>
    {
        var policy = await db.PasswordPolicies.AsNoTracking().SingleAsync();
        return Results.Ok(PasswordPolicyDto.From(policy));
    }
);
adminApi.MapPut(
    "/password-policy",
    async (PasswordPolicyDto request, AppDbContext db) =>
    {
        if (request.RequiredLength is < 8 or > 128)
            return Results.BadRequest(new { detail = "パスワードの最小文字数は8～128文字で指定してください。" });
        var policy = await db.PasswordPolicies.SingleAsync();
        policy.RequiredLength = request.RequiredLength;
        policy.RequireDigit = request.RequireDigit;
        policy.RequireLowercase = request.RequireLowercase;
        policy.RequireUppercase = request.RequireUppercase;
        policy.RequireNonAlphanumeric = request.RequireNonAlphanumeric;
        await db.SaveChangesAsync();
        return Results.Ok(PasswordPolicyDto.From(policy));
    }
);
adminApi.MapGet(
    "/login-attempt-policy",
    async (ILoginAttemptPolicyService service, CancellationToken cancellationToken) =>
    {
        var policy = await service.GetAsync(cancellationToken);
        return Results.Ok(LoginAttemptPolicyDto.From(policy));
    }
);
adminApi.MapPut(
    "/login-attempt-policy",
    async (
        LoginAttemptPolicyDto request,
        ILoginAttemptPolicyService service,
        CancellationToken cancellationToken
    ) =>
    {
        if (request.MaxFailedAttempts is < 1 or > 100)
            return Results.BadRequest(new { detail = "ログイン試行回数は1～100回で指定してください。" });
        var policy = await service.UpdateAsync(
            new LoginAttemptPolicy(request.IsEnabled, request.MaxFailedAttempts),
            cancellationToken
        );
        return Results.Ok(LoginAttemptPolicyDto.From(policy));
    }
);
adminApi.MapPost(
    "/users",
    async (CreateUserRequest request, UserManager<ApplicationUser> users) =>
    {
        if (!ValidRole(request.Role) || string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.BadRequest(new { detail = "表示名と有効なロールを指定してください。" });
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            IsActive = true,
        };
        var created = await users.CreateAsync(user, request.TemporaryPassword);
        if (!created.Succeeded)
            return IdentityProblem(created);
        var role = await users.AddToRoleAsync(user, request.Role);
        if (!role.Succeeded)
        {
            await users.DeleteAsync(user);
            return IdentityProblem(role);
        }
        return Results.Created($"/api/admin/users/{user.Id}", await UserDto.FromAsync(user, users));
    }
);
adminApi.MapPut(
    "/users/{id:guid}",
    async (
        Guid id,
        UpdateUserRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        UserManager<ApplicationUser> users
    ) =>
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.NotFound();
        if (!ValidRole(request.Role) || string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.BadRequest(new { detail = "表示名と有効なロールを指定してください。" });
        var viewerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentRoles = await users.GetRolesAsync(user);
        var isAdmin = currentRoles.Contains("Admin");
        try
        {
            AdministrationRules.ValidateUserStateChange(
                viewerId,
                id,
                isAdmin,
                await ActiveAdminCount(db, users),
                request.IsActive,
                request.Role == "Admin" ? UserRole.Admin : UserRole.User
            );
        }
        catch (DomainException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
        user.DisplayName = request.DisplayName.Trim();
        user.IsActive = request.IsActive;
        var emailResult = await users.SetEmailAsync(user, request.Email.Trim());
        if (!emailResult.Succeeded)
            return IdentityProblem(emailResult);
        var nameResult = await users.SetUserNameAsync(user, request.Email.Trim());
        if (!nameResult.Succeeded)
            return IdentityProblem(nameResult);
        foreach (var role in currentRoles.Where(x => x != request.Role))
        {
            var removed = await users.RemoveFromRoleAsync(user, role);
            if (!removed.Succeeded)
                return IdentityProblem(removed);
        }
        if (!await users.IsInRoleAsync(user, request.Role))
        {
            var added = await users.AddToRoleAsync(user, request.Role);
            if (!added.Succeeded)
                return IdentityProblem(added);
        }
        var updated = await users.UpdateAsync(user);
        return updated.Succeeded
            ? Results.Ok(await UserDto.FromAsync(user, users))
            : IdentityProblem(updated);
    }
);
adminApi.MapPost(
    "/users/{id:guid}/temporary-password",
    async (Guid id, ResetPasswordRequest request, UserManager<ApplicationUser> users) =>
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.NotFound();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, request.TemporaryPassword);
        return result.Succeeded ? Results.NoContent() : IdentityProblem(result);
    }
);
adminApi.MapGet(
    "/resources",
    async (string? search, ResourceKind? kind, AppDbContext db) =>
    {
        var query = db.Resources.AsNoTracking();
        if (kind is not null)
            query = query.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search.Trim()));
        return Results.Ok(
            await query
                .OrderBy(x => x.Kind)
                .ThenBy(x => x.Name)
                .Take(200)
                .Select(ResourceDto.Expression)
                .ToListAsync()
        );
    }
);
adminApi.MapPost(
    "/resources",
    async (ResourceWriteRequest request, AppDbContext db) =>
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Results.BadRequest(new { detail = "名称は1〜100文字で入力してください。" });
        var resource = new ResourceEntity { Name = request.Name.Trim(), Kind = request.Kind };
        db.Resources.Add(resource);
        await db.SaveChangesAsync();
        return Results.Created($"/api/admin/resources/{resource.Id}", ResourceDto.From(resource));
    }
);
adminApi.MapPut(
    "/resources/{id:guid}",
    async (Guid id, ResourceWriteRequest request, AppDbContext db) =>
    {
        var resource = await db.Resources.FindAsync(id);
        if (resource is null)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Results.BadRequest(new { detail = "名称は1〜100文字で入力してください。" });
        if (request.Version != resource.Version)
            return Results.Conflict(
                new { detail = "別のユーザーにより更新されています。再読み込みしてください。" }
            );
        resource.Name = request.Name.Trim();
        resource.Kind = request.Kind;
        resource.IsActive = request.IsActive;
        resource.Version++;
        await db.SaveChangesAsync();
        return Results.Ok(ResourceDto.From(resource));
    }
);

api.MapGet(
        "/targets",
        async (AppDbContext db, UserManager<ApplicationUser> users) =>
        {
            // Keep archived users available for historical schedules and saved timelines.
            // The client excludes them from new participant and timeline selections.
            var availableUsers = await db.Users.ToListAsync();
            var dtos = new List<UserDto>();
            foreach (var user in availableUsers)
                dtos.Add(await UserDto.FromAsync(user, users));
            var resources = await db
                .Resources
                .Select(ResourceDto.Expression)
                .ToListAsync();
            return Results.Ok(new { users = dtos, resources });
        }
    )
    .RequireAuthorization();
api.MapGet(
        "/timeline-preferences",
        async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var ownerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var rows = await db
                .UserTimelinePreferences.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerId)
                .OrderBy(x => x.SortOrder)
                .ToListAsync();
            return Results.Ok(
                new TimelinePreferenceDto([
                    new("User", ownerId),
                    .. rows.Select(x =>
                        x.TargetUserId.HasValue
                            ? new TimelineTargetRequest("User", x.TargetUserId.Value, x.IsVisible)
                            : new TimelineTargetRequest(
                                "Resource",
                                x.TargetResourceId!.Value,
                                x.IsVisible
                            )
                    ),
                ])
            );
        }
    )
    .RequireAuthorization();
api.MapPut(
        "/timeline-preferences",
        async (TimelinePreferenceRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            if (
                request.Targets.Length > 100
                || request.Targets.Select(x => x.Id).Distinct().Count() != request.Targets.Length
            )
                return Results.BadRequest(new { detail = "表示対象が不正または多すぎます。" });
            var ownerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var requestedUsers = request
                .Targets.Where(x => x.Kind == "User" && x.Id != ownerId)
                .Select(x => x.Id)
                .ToArray();
            var requestedResources = request
                .Targets.Where(x => x.Kind == "Resource")
                .Select(x => x.Id)
                .ToArray();
            if (request.Targets.Any(x => x.Kind is not ("User" or "Resource")))
                return Results.BadRequest(new { detail = "表示対象の種類が不正です。" });
            if (
                await db.Users.CountAsync(x => requestedUsers.Contains(x.Id))
                    != requestedUsers.Length
                || await db.Resources.CountAsync(x => requestedResources.Contains(x.Id))
                    != requestedResources.Length
            )
                return Results.BadRequest(
                    new { detail = "存在しない表示対象は追加できません。" }
                );
            var existing = await db
                .UserTimelinePreferences.Where(x => x.OwnerUserId == ownerId)
                .ToListAsync();
            db.UserTimelinePreferences.RemoveRange(existing);
            var order = 0;
            foreach (var target in request.Targets.Where(x => x.Id != ownerId))
                db.UserTimelinePreferences.Add(
                    new UserTimelinePreferenceEntity
                    {
                        OwnerUserId = ownerId,
                        TargetUserId = target.Kind == "User" ? target.Id : null,
                        TargetResourceId = target.Kind == "Resource" ? target.Id : null,
                        SortOrder = order++,
                        IsVisible = target.IsVisible,
                    }
                );
            await db.SaveChangesAsync();
            return Results.Ok(
                new TimelinePreferenceDto([
                    new("User", ownerId),
                    .. request.Targets.Where(x => x.Id != ownerId),
                ])
            );
        }
    )
    .RequireAuthorization();
api.MapGet(
        "/notifications",
        async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var recipientId = Guid.Parse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier)!
            );
            var unreadCount = await db.Notifications.CountAsync(x =>
                x.RecipientUserId == recipientId && !x.IsRead
            );
            var notifications = await db
                .Notifications.AsNoTracking()
                .Where(x => x.RecipientUserId == recipientId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(100)
                .Select(x => new NotificationDto(
                    x.Id,
                    x.ActorUser.DisplayName,
                    x.ScheduleTitle,
                    x.ScheduleId,
                    x.CreatedAtUtc,
                    x.IsRead
                ))
                .ToListAsync();
            return Results.Ok(
                new NotificationListDto(
                    unreadCount,
                    notifications.ToArray()
                )
            );
        }
    )
    .RequireAuthorization();
api.MapPost(
        "/notifications/read",
        async (NotificationReadRequest request, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var recipientId = Guid.Parse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier)!
            );
            var query = db.Notifications.Where(x => x.RecipientUserId == recipientId);
            if (request.Ids is { Length: > 0 })
                query = query.Where(x => request.Ids.Contains(x.Id));
            await query.Where(x => !x.IsRead).ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.IsRead, true)
            );
            return Results.NoContent();
        }
    )
    .RequireAuthorization();
api.MapGet(
        "/schedules",
        async (
            DateTimeOffset from,
            DateTimeOffset to,
            string? targets,
            ClaimsPrincipal principal,
            AppDbContext db
        ) =>
        {
            if (to <= from || to - from > TimeSpan.FromDays(31))
                return Results.BadRequest(
                    new { detail = "日時範囲は31日以内で指定してください。" }
                );
            var viewerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var admin = principal.IsInRole("Admin");
            var targetIds = (targets ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty)
                .Where(x => x != Guid.Empty)
                .Append(viewerId)
                .Distinct()
                .Take(101)
                .ToArray();
            if (targetIds.Length > 100)
                return Results.BadRequest(new { detail = "表示対象は100件以内にしてください。" });
            var entities = await ScheduleQuery(db)
                .AsNoTracking()
                .Where(x =>
                    x.StartsAtUtc < to.UtcDateTime
                    && from.UtcDateTime < x.EndsAtUtc
                    && (
                        x.Participants.Any(p => targetIds.Contains(p.UserId))
                        || x.Resources.Any(r => targetIds.Contains(r.ResourceId))
                    )
                )
                .OrderBy(x => x.StartsAtUtc)
                .ToListAsync();
            var rows = entities.Select(ToDomain).ToArray();
            var lanes = TimelineLaneAllocator.Allocate(rows);
            return Results.Ok(
                rows.Select(x => SchedulePrivacy.ToView(x, viewerId, admin, lanes[x.Id]))
            );
        }
    )
    .RequireAuthorization();
api.MapGet(
        "/schedules/{id:guid}",
        async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var entity = await ScheduleQuery(db)
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);
            if (entity is null)
                return Results.NotFound();
            var viewerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Results.Ok(
                SchedulePrivacy.ToView(ToDomain(entity), viewerId, principal.IsInRole("Admin"))
            );
        }
    )
    .RequireAuthorization();
api.MapPost(
        "/schedules",
        async (
            CreateScheduleRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            ScheduleRealtimeHub realtime
        ) =>
        {
            var viewerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var item = request.ToSchedule(viewerId);
            var invalid = await ValidateSchedule(item, db);
            if (invalid is not null)
                return Results.BadRequest(new { detail = invalid });
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable
            );
            var conflicts = await FindConflicts(item, null, db);
            if (ConflictDecision.MustWarn(conflicts.Count, request.ConfirmConflicts))
                return ConflictResult(conflicts, viewerId, principal.IsInRole("Admin"));
            var entity = ToEntity(item);
            db.Schedules.Add(entity);
            foreach (var recipientId in item.ParticipantIds.Where(id => id != viewerId))
                db.Notifications.Add(
                    new NotificationEntity
                    {
                        RecipientUserId = recipientId,
                        ActorUserId = viewerId,
                        ScheduleId = item.Id,
                        ScheduleTitle = item.Title,
                    }
                );
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await realtime.BroadcastScheduleChangedAsync(item.Id, "created");
            return Results.Created(
                $"/api/schedules/{item.Id}",
                SchedulePrivacy.ToView(item, viewerId, principal.IsInRole("Admin"))
            );
        }
    )
    .RequireAuthorization();
api.MapPut(
        "/schedules/{id:guid}",
        async (
            Guid id,
            UpdateScheduleRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            ScheduleRealtimeHub realtime
        ) =>
        {
            var entity = await ScheduleQuery(db).SingleOrDefaultAsync(x => x.Id == id);
            if (entity is null)
                return Results.NotFound();
            var viewerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (entity.CreatedById != viewerId)
                return Results.Forbid();
            if (entity.Version != request.Version)
                return Results.Conflict(
                    new
                    {
                        detail = "別のユーザーにより予定が更新されています。再読み込みしてください。",
                    }
                );
            var existingParticipantIds = entity
                .Participants.Select(participant => participant.UserId)
                .ToHashSet();
            var item = request.ToSchedule(id, entity.CreatedById, entity.CreatedAtUtc);
            var invalid = await ValidateSchedule(
                item,
                db,
                existingParticipantIds,
                entity.Resources.Select(resource => resource.ResourceId).ToHashSet()
            );
            if (invalid is not null)
                return Results.BadRequest(new { detail = invalid });
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable
            );
            var conflicts = await FindConflicts(item, id, db);
            if (ConflictDecision.MustWarn(conflicts.Count, request.ConfirmConflicts))
                return ConflictResult(conflicts, viewerId, principal.IsInRole("Admin"));
            entity.Title = item.Title;
            entity.Details = item.Details;
            entity.StartsAtUtc = item.StartsAtUtc.UtcDateTime;
            entity.EndsAtUtc = item.EndsAtUtc.UtcDateTime;
            entity.IsPrivate = item.IsPrivate;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.Version++;
            entity.Participants.Clear();
            foreach (var userId in item.ParticipantIds)
                entity.Participants.Add(
                    new ScheduleParticipantEntity { ScheduleId = id, UserId = userId }
                );
            entity.Resources.Clear();
            foreach (var resourceId in item.ResourceIds)
                entity.Resources.Add(
                    new ScheduleResourceEntity { ScheduleId = id, ResourceId = resourceId }
                );
            foreach (
                var recipientId in item.ParticipantIds.Where(participantId =>
                    participantId != viewerId && !existingParticipantIds.Contains(participantId)
                )
            )
                db.Notifications.Add(
                    new NotificationEntity
                    {
                        RecipientUserId = recipientId,
                        ActorUserId = viewerId,
                        ScheduleId = item.Id,
                        ScheduleTitle = item.Title,
                    }
                );
            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(
                    new { detail = "別のユーザーにより予定が更新されています。" }
                );
            }
            item.Version = entity.Version;
            await realtime.BroadcastScheduleChangedAsync(id, "updated");
            return Results.Ok(SchedulePrivacy.ToView(item, viewerId, principal.IsInRole("Admin")));
        }
    )
    .RequireAuthorization();
api.MapDelete(
        "/schedules/{id:guid}",
        async (
            Guid id,
            long version,
            ClaimsPrincipal principal,
            AppDbContext db,
            ScheduleRealtimeHub realtime
        ) =>
        {
            var entity = await db.Schedules.SingleOrDefaultAsync(x => x.Id == id);
            if (entity is null)
                return Results.NotFound();
            var viewerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (entity.CreatedById != viewerId)
                return Results.Forbid();
            if (entity.Version != version)
                return Results.Conflict(
                    new { detail = "別のユーザーにより予定が更新されています。" }
                );
            db.Schedules.Remove(entity);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(
                    new { detail = "別のユーザーにより予定が更新されています。" }
                );
            }
            await realtime.BroadcastScheduleChangedAsync(id, "deleted");
            return Results.NoContent();
        }
    )
    .RequireAuthorization();

static bool ValidRole(string role) => role is "Admin" or "User";
static IResult IdentityProblem(IdentityResult result) =>
    Results.BadRequest(new { detail = string.Join(" ", result.Errors.Select(LocalizeIdentityError)) });
static string LocalizeIdentityError(IdentityError error) =>
    error.Code switch
    {
        "DuplicateEmail" => "このメールアドレスは既に登録されています。",
        "DuplicateUserName" => "このメールアドレスは既に登録されています。",
        "InvalidEmail" => "有効なメールアドレスを入力してください。",
        "InvalidUserName" => "メールアドレスに使用できない文字が含まれています。",
        "PasswordTooShort" => "パスワードが短すぎます。",
        "PasswordRequiresDigit" => "パスワードには数字を1文字以上含めてください。",
        "PasswordRequiresLower" => "パスワードには小文字を1文字以上含めてください。",
        "PasswordRequiresUpper" => "パスワードには大文字を1文字以上含めてください。",
        "PasswordRequiresNonAlphanumeric" => "パスワードには記号を1文字以上含めてください。",
        _ => error.Description,
    };
static async Task<int> ActiveAdminCount(AppDbContext db, UserManager<ApplicationUser> users)
{
    var active = await db.Users.Where(x => x.IsActive).ToListAsync();
    var count = 0;
    foreach (var user in active)
        if (await users.IsInRoleAsync(user, "Admin"))
            count++;
    return count;
}
static IQueryable<ScheduleEntity> ScheduleQuery(AppDbContext db) =>
    db.Schedules.Include(x => x.Participants).Include(x => x.Resources);
static Schedule ToDomain(ScheduleEntity x) =>
    new()
    {
        Id = x.Id,
        Title = x.Title,
        Details = x.Details,
        StartsAtUtc = new DateTimeOffset(DateTime.SpecifyKind(x.StartsAtUtc, DateTimeKind.Utc)),
        EndsAtUtc = new DateTimeOffset(DateTime.SpecifyKind(x.EndsAtUtc, DateTimeKind.Utc)),
        IsPrivate = x.IsPrivate,
        CreatedById = x.CreatedById,
        ParticipantIds = x.Participants.Select(p => p.UserId).ToHashSet(),
        ResourceIds = x.Resources.Select(r => r.ResourceId).ToHashSet(),
        CreatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc)),
        UpdatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(x.UpdatedAtUtc, DateTimeKind.Utc)),
        Version = x.Version,
    };
static ScheduleEntity ToEntity(Schedule x) =>
    new()
    {
        Id = x.Id,
        Title = x.Title,
        Details = x.Details,
        StartsAtUtc = x.StartsAtUtc.UtcDateTime,
        EndsAtUtc = x.EndsAtUtc.UtcDateTime,
        IsPrivate = x.IsPrivate,
        CreatedById = x.CreatedById,
        CreatedAtUtc = x.CreatedAtUtc.UtcDateTime,
        UpdatedAtUtc = x.UpdatedAtUtc.UtcDateTime,
        Version = x.Version,
        Participants = x
            .ParticipantIds.Select(id => new ScheduleParticipantEntity
            {
                ScheduleId = x.Id,
                UserId = id,
            })
            .ToList(),
        Resources = x
            .ResourceIds.Select(id => new ScheduleResourceEntity
            {
                ScheduleId = x.Id,
                ResourceId = id,
            })
            .ToList(),
    };
static async Task<string?> ValidateSchedule(
    Schedule item,
    AppDbContext db,
    IReadOnlySet<Guid>? existingParticipantIds = null,
    IReadOnlySet<Guid>? existingResourceIds = null
)
{
    try
    {
        item.Validate();
    }
    catch (DomainException ex)
    {
        return ex.Message;
    }
    var allowedUsers = await db.Users.CountAsync(x =>
        item.ParticipantIds.Contains(x.Id)
        && (x.IsActive || (existingParticipantIds != null && existingParticipantIds.Contains(x.Id)))
    );
    if (allowedUsers != item.ParticipantIds.Count)
        return "アーカイブ済み、または存在しないユーザーは予定へ新しく追加できません。";
    var allowedResources = await db.Resources.CountAsync(x =>
        item.ResourceIds.Contains(x.Id)
        && (x.IsActive || (existingResourceIds != null && existingResourceIds.Contains(x.Id)))
    );
    if (allowedResources != item.ResourceIds.Count)
        return "アーカイブ済み、または存在しない会議室・備品は新しく予約できません。";
    return null;
}
static async Task<List<Schedule>> FindConflicts(Schedule item, Guid? excludedId, AppDbContext db)
{
    var participantIds = item.ParticipantIds.ToArray();
    var resourceIds = item.ResourceIds.ToArray();
    var start = item.StartsAtUtc.UtcDateTime;
    var end = item.EndsAtUtc.UtcDateTime;
    var rows = await ScheduleQuery(db)
        .AsNoTracking()
        .Where(x =>
            x.Id != excludedId
            && x.StartsAtUtc < end
            && start < x.EndsAtUtc
            && (
                x.Participants.Any(p => participantIds.Contains(p.UserId))
                || x.Resources.Any(r => resourceIds.Contains(r.ResourceId))
            )
        )
        .ToListAsync();
    return rows.Select(ToDomain).ToList();
}
static IResult ConflictResult(IEnumerable<Schedule> conflicts, Guid viewerId, bool isAdmin) =>
    Results.Conflict(
        new
        {
            detail = "選択した対象には、同じ時間帯に別の予定があります。それでも登録しますか？",
            conflicts = conflicts.Select(x => SchedulePrivacy.ToView(x, viewerId, isAdmin)),
        }
    );

app.MapFallbackToFile("index.html");
app.Run();

record LoginRequest(string Email, string Password);

record CreateUserRequest(string DisplayName, string Email, string Role, string TemporaryPassword);

record UpdateUserRequest(string DisplayName, string Email, string Role, bool IsActive);

record ResetPasswordRequest(string TemporaryPassword);

record PasswordPolicyDto(
    int RequiredLength,
    bool RequireDigit,
    bool RequireLowercase,
    bool RequireUppercase,
    bool RequireNonAlphanumeric
)
{
    public static PasswordPolicyDto From(PasswordPolicyEntity policy) =>
        new(
            policy.RequiredLength,
            policy.RequireDigit,
            policy.RequireLowercase,
            policy.RequireUppercase,
            policy.RequireNonAlphanumeric
        );
}

record LoginAttemptPolicyDto(bool IsEnabled, int MaxFailedAttempts)
{
    public static LoginAttemptPolicyDto From(LoginAttemptPolicy policy) =>
        new(policy.IsEnabled, policy.MaxFailedAttempts);
}

record ResourceWriteRequest(string Name, ResourceKind Kind, bool IsActive = true, long Version = 0);

record TimelineTargetRequest(string Kind, Guid Id, bool IsVisible = true);

record TimelinePreferenceRequest(TimelineTargetRequest[] Targets);

record TimelinePreferenceDto(TimelineTargetRequest[] Targets);

record NotificationReadRequest(Guid[]? Ids);

record NotificationDto(
    Guid Id,
    string ActorDisplayName,
    string ScheduleTitle,
    Guid ScheduleId,
    DateTime CreatedAtUtc,
    bool IsRead
);

record NotificationListDto(int UnreadCount, NotificationDto[] Items);

record CreateScheduleRequest(
    string Title,
    string? Details,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    bool IsPrivate,
    Guid[] ParticipantIds,
    Guid[] ResourceIds,
    bool ConfirmConflicts
)
{
    public Schedule ToSchedule(Guid creator) =>
        new()
        {
            Title = Title,
            Details = Details ?? "",
            StartsAtUtc = StartsAtUtc.ToUniversalTime(),
            EndsAtUtc = EndsAtUtc.ToUniversalTime(),
            IsPrivate = IsPrivate,
            CreatedById = creator,
            ParticipantIds = ParticipantIds.ToHashSet(),
            ResourceIds = ResourceIds.ToHashSet(),
        };
}

record UpdateScheduleRequest(
    string Title,
    string? Details,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    bool IsPrivate,
    Guid[] ParticipantIds,
    Guid[] ResourceIds,
    bool ConfirmConflicts,
    long Version
)
{
    public Schedule ToSchedule(Guid id, Guid creator, DateTime createdAtUtc) =>
        new()
        {
            Id = id,
            Title = Title,
            Details = Details ?? "",
            StartsAtUtc = StartsAtUtc.ToUniversalTime(),
            EndsAtUtc = EndsAtUtc.ToUniversalTime(),
            IsPrivate = IsPrivate,
            CreatedById = creator,
            ParticipantIds = ParticipantIds.ToHashSet(),
            ResourceIds = ResourceIds.ToHashSet(),
            CreatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Version = Version,
        };
}

record UserDto(Guid Id, string DisplayName, string Email, string Role, bool IsActive)
{
    public static async Task<UserDto> FromAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> users
    ) =>
        new(
            user.Id,
            user.DisplayName,
            user.Email!,
            (await users.GetRolesAsync(user)).FirstOrDefault() ?? "User",
            user.IsActive
        );
}

record ResourceDto(Guid Id, string Name, ResourceKind Kind, bool IsActive, long Version)
{
    public static readonly System.Linq.Expressions.Expression<
        Func<ResourceEntity, ResourceDto>
    > Expression = x => new(x.Id, x.Name, x.Kind, x.IsActive, x.Version);

    public static ResourceDto From(ResourceEntity x) =>
        new(x.Id, x.Name, x.Kind, x.IsActive, x.Version);
}

public partial class Program;
