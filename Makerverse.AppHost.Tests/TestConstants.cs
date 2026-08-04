namespace Makerverse.AppHost.Tests;

/// <summary>
///     固定参数与凭据（TESTING.md §5）、超时（§8）与资源名（AppHost.cs 实测）。
/// </summary>
public static class TestConstants {
    // AppHost 测试参数（TESTING.md §5）
    public const string KeycloakPasswordParameter  = "keycloak-password";
    public const string KeycloakAdminPassword      = "test-admin-password";
    public const string AccountServiceClientSecret = "test-account-service-secret";
    public const string TypesenseApiKey            = "test-typesense-key";

    // Keycloak 测试 realm（TESTING.md §7）
    public const string Realm             = "makerverse";
    public const string PublicClientId    = "makerverse";
    public const string AdminClientId     = "makerverse-account-service";
    public const string AdminCliClientId  = "admin-cli";
    public const string TestUserName      = "testuser";
    public const string TestAdminUserName = "testadmin";
    public const string TestPassword      = "test-password";

    // TagsController 使用 [Authorize(Roles = "Admin")]，且 .NET 的 IsInRole 为区分大小写的 ordinal 比较，
    // 因此 realm 角色名必须与代码一致（"Admin"）。实测 "admin" 会 403。
    public const string AdminRoleName = "Admin";

    // 全栈资源（TESTING.md §9 S2）。RabbitMQ 实际资源名为 messaging（AppHost.cs 实测）。
    public static readonly string[] StackResources = [
        "account-svc", "activity-svc", "live-svc", "search-svc",
        "gateway", "keycloak", "postgres", "redis", "messaging",
        "minio", "typesense", "livestream-svc"
    ];

    public static string[] AppHostArgs { get; } = [
        "--environment=Development",
        $"Parameters:{KeycloakPasswordParameter}={KeycloakAdminPassword}",
        $"Parameters:account-service-client-secret={AccountServiceClientSecret}",
        $"Parameters:typesense-api-key={TypesenseApiKey}",
        // 固定 postgres 密码：保留 postgres-data 卷时避免旧卷密码不匹配（TESTING.md §2-1），
        // 并让数据目录跨轮预热（消除 Aspire 建库脚本与首次初始化的竞态挂起）。
        "Parameters:postgres-password=test-postgres-password"
    ];

    // 超时（TESTING.md §8）：CI → 5 分钟；本地默认 30 秒；
    // 全栈健康等待本地放宽到 10 分钟（§8 首次运行预算 5–10 分钟，§11 Keycloak 冷启动 30–60s + livestream 镜像构建）。
    public static bool IsCi => Environment.GetEnvironmentVariable("CI") is not null;

    public static TimeSpan DefaultTimeout => IsCi ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30);

    public static TimeSpan StackHealthyTimeout => IsCi ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(10);

    /// <summary>Wolverine/RabbitMQ 异步消息轮询上限（TESTING.md §9 SearchFlowTests）。</summary>
    public static TimeSpan SearchPollTimeout => TimeSpan.FromSeconds(30);

    /// <summary>直播从 Created → start → 会话 TTL 30s 过期 → watcher 置 Stopped 的等待上限（LiveFlowTests L5）。</summary>
    public static TimeSpan LiveStoppedTimeout => TimeSpan.FromSeconds(120);

    /// <summary>用例数据隔离后缀（TESTING.md §8）。</summary>
    public static string UniqueSuffix() {
        return Guid.NewGuid().ToString("N")[..8];
    }
}