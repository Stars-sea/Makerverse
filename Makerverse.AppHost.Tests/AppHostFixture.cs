using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Projects;

namespace Makerverse.AppHost.Tests;

/// <summary>
///     单个 xUnit collection fixture：全测试类共享一个 AppHost 实例（TESTING.md §4 共享实例策略）。
///     流程：参数覆盖构建（§5）→ 移除 ContainerMountAnnotation（§6-1）→ 启动 → 全栈 healthy（§8/§9 S2）→ Keycloak realm 自助建立（§7）。
/// </summary>
[CollectionDefinition("AppHost")]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>;

public sealed class AppHostFixture : IAsyncLifetime {
    public DistributedApplication App { get; private set; } = null!;

    public KeycloakTestRealm Keycloak { get; private set; } = null!;

    /// <summary>YARP 网关（routes: /activities /tags /lives /search，AppHost.cs 实测）。</summary>
    public HttpClient Gateway { get; private set; } = null!;

    /// <summary>account-svc 直连（/account/* 不在网关路由内）。</summary>
    public HttpClient AccountService { get; private set; } = null!;

    public HttpClient ActivityService { get; private set; } = null!;

    public HttpClient LiveService { get; private set; } = null!;

    public HttpClient SearchService { get; private set; } = null!;

    public async Task InitializeAsync() {
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Makerverse_AppHost>(TestConstants.AppHostArgs);

        DistributedApplication app = await builder.BuildAsync().WaitAsync(TestConstants.DefaultTimeout);

        RemoveContainerMounts(app);
        RemoveDockerfileBuildInCi(app);
        LimitKeycloakHeapInCi(app);

        await app.StartAsync().WaitAsync(TestConstants.DefaultTimeout);
        App = app;

        Gateway         = app.CreateHttpClient("gateway");
        AccountService  = app.CreateHttpClient("account-svc");
        ActivityService = app.CreateHttpClient("activity-svc");
        LiveService     = app.CreateHttpClient("live-svc");
        SearchService   = app.CreateHttpClient("search-svc");

        await WaitForAllHealthyAsync();

        Keycloak = new KeycloakTestRealm(app);
        await Keycloak.InitializeAsync();
    }

    public async Task DisposeAsync() {
        if (App is not null) await App.DisposeAsync();
    }

    /// <summary>TESTING.md §9 S2：依次等待全栈资源 healthy（无健康检查注解的资源在 Running 即视为 healthy）。</summary>
    public async Task WaitForAllHealthyAsync() {
        using CancellationTokenSource cts = new(TestConstants.StackHealthyTimeout);
        foreach (string name in TestConstants.StackResources)
            await App.ResourceNotifications.WaitForResourceHealthyAsync(name, cts.Token);
    }

    public Task<(string AccessToken, string RefreshToken)> LoginAsync(string username, string password) {
        return TestHttp.LoginAsync(AccountService, username, password);
    }

    /// <summary>
    ///     TESTING.md §8：CI 下限制 keycloak JVM 堆。镜像默认 KC_RUN_IN_CONTAINER=true →
    ///     kc.sh 用 -XX:InitialRAMPercentage=50/-XX:MaxRAMPercentage=70（实测 26.6 镜像），
    ///     GitHub runner 7GB 内存下 keycloak 启动即 commit ~3.5GB，全栈合计逼近上限，
    ///     OOM killer 会杀掉 keycloak 容器（FailedToStart，实测 2026-08-05 CI 失败）。
    ///     注入 JAVA_OPTS_KC_HEAP 覆盖为固定堆（kc.sh 优先级：环境变量 &gt; 镜像默认）。
    /// </summary>
    private static void LimitKeycloakHeapInCi(DistributedApplication app) {
        if (!TestConstants.IsCi) return;

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        foreach (IResource resource in model.Resources) {
            if (resource.Name != "keycloak") continue;

            resource.Annotations.Add(new EnvironmentCallbackAnnotation(
                "JAVA_OPTS_KC_HEAP",
                () => "-Xms128m -Xmx1g"));
        }
    }

    /// <summary>
    ///     TESTING.md §8：CI 下移除 livestream-svc 的 Dockerfile 构建注解——DCP 只要存在
    ///     DockerfileBuildAnnotation 就会在每次 StartAsync 无条件执行 docker build（DCP v0.24.3
    ///     handleNewContainer/buildImageWithOrchestrator），冷 runner 上 cargo-chef 多阶段 release
    ///     构建远超预算。镜像由 CI 工作流提供并 tag 为 livestream-svc:latest（优先拉子仓库 GHCR
    ///     包，miss 时本地预构建），测试直接复用。
    /// </summary>
    private static void RemoveDockerfileBuildInCi(DistributedApplication app) {
        if (!TestConstants.IsCi) return;

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        foreach (IResource resource in model.Resources) {
            if (resource.Name != "livestream-svc") continue;

            foreach (DockerfileBuildAnnotation annotation in
                     resource.Annotations.OfType<DockerfileBuildAnnotation>().ToList()) {
                resource.Annotations.Remove(annotation);
            }
        }
    }

    /// <summary>
    ///     TESTING.md §6-1：构建后、启动前移除资源的命名卷与 bind mount，保证每轮测试干净。
    ///     例外（实测调整）：
    ///     1. postgres：保留命名卷（postgres-data）+ 固定密码（TestConstants）——数据目录预热，
    ///     消除 Aspire 建库脚本（docker exec psql）与首次初始化的竞态挂起（实测 StartAsync 偶发卡死 5 分钟）；
    ///     仅移除 data/postgres 的 bind mount（该目录不存在，挂载会创建空目录，移除后由 Aspire 脚本建库）。
    ///     2. typesense：TYPESENSE_DATA_DIR=/data 依赖挂载创建目录（镜像内不存在），
    ///     移除命名卷后补匿名卷挂载——每轮测试全新且目录存在（实测：无挂载时 typesense 30.1 拒绝启动）。
    /// </summary>
    private static void RemoveContainerMounts(DistributedApplication app) {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        foreach (IResource resource in model.Resources) {
            foreach (ContainerMountAnnotation mount in
                     resource.Annotations.OfType<ContainerMountAnnotation>().ToList()) {
                bool keepPostgresVolume = resource.Name == "postgres" && mount.Type == ContainerMountType.Volume;
                if (!keepPostgresVolume) resource.Annotations.Remove(mount);
            }

            if (resource.Name == "typesense")
                resource.Annotations.Add(new ContainerMountAnnotation(null, "/data", ContainerMountType.Volume, false));
        }
    }
}