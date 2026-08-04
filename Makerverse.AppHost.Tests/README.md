# Makerverse.AppHost.Tests

后端测试套件：Aspire 封闭式 E2E（`Aspire.Hosting.Testing` + `DistributedApplicationTestingBuilder`，进程外全栈启动，不可 mock DI）+ Tier 1 单元测试。

## 1. 运行

前置：Docker 可用（podman 亦可，需 `docker.sock` 兼容）、.NET SDK 10、Aspire CLI 13.4.6。

```bash
dotnet test                     # 仓库根；首次运行拉取镜像并本地构建 livestream-svc，预算 5–10 分钟
dotnet test --filter SmokeTests # 单组
```

- 超时（依据 aspire.dev testing-in-cicd-pipelines）：`CI` 环境变量存在 → 5 分钟；本地 → 30 秒。全栈健康等待本地放宽到 10 分钟（首次运行预算）。
- 端口随机化（`DcpPublisher:RandomizePorts` 默认 true），并行安全；同一 collection 内测试串行。

## 2. 测试分层

| 层 | 技术 | 目标 | 状态 |
|---|---|---|---|
| Tier 1 单元 | xUnit，无基础设施 | 纯逻辑：校验器、错误映射 | 已实现（`UnitTests/`） |
| Tier 2 服务级集成 | `WebApplicationFactory<T>` + Testcontainers（单服务自有基础设施） | 隔离验证单服务 | 未实现（可选扩展，见 §8） |
| Tier 3 Aspire E2E | `Aspire.Hosting.Testing` 全栈 | 跨服务真实链路（核心） | 已实现 |

## 3. 项目结构

```
Makerverse.AppHost.Tests/
  Makerverse.AppHost.Tests.csproj
  GlobalUsings.cs
  TestConstants.cs              # 固定参数/凭据/超时/资源名
  TestHelpers.cs                # 登录、带 Bearer 请求、统一轮询、极小 PNG
  AppHostFixture.cs             # IAsyncLifetime 共享 AppHost；参数覆盖；mount 清理；全栈健康等待
  KeycloakTestRealm.cs          # Admin REST 建 realm/client/user，幂等
  SmokeTests.cs                 # S1-S3 健康/可达性
  AuthFlowTests.cs              # A1-A6 注册/登录/me/资料/头像/刷新
  ActivityFlowTests.cs          # C1-C7 活动/评论/标签/Redis 缓存
  LiveFlowTests.cs              # L1-L5 直播 CRUD + HLS 段
  SearchFlowTests.cs            # M1-M6 消息→Typesense 索引、搜索接口
  UnitTests/
    ValidatorTests.cs           # U1/U2
    ErrorMappingTests.cs        # U3
```

共享实例策略：单个 xUnit collection（`[CollectionDefinition("AppHost")]` + `ICollectionFixture<AppHostFixture>`）全类共享一个 AppHost 实例（AppHost 昂贵，一次构建多测试复用）；用例之间以唯一后缀（`Guid.NewGuid().ToString("N")[..8]`）隔离数据，避免共享状态断言冲突。

## 4. Fixture 设计

### 启动参数（TestConstants.AppHostArgs）

```
--environment=Development
Parameters:keycloak-password=test-admin-password
Parameters:account-service-client-secret=test-account-service-secret
Parameters:typesense-api-key=test-typesense-key
Parameters:postgres-password=test-postgres-password
```

流程：构建（`BuildAsync`）→ mount 清理 → `StartAsync` → 全栈 healthy（`WaitForResourceHealthyAsync`，见 S2）→ Keycloak realm 建立。

### Mount 处理（实测修正）

- 构建后、启动前移除所有资源的 `ContainerMountAnnotation`（命名卷与 bind mount），保证每轮测试干净。
- **postgres 例外**：保留 `postgres-data` 命名卷 + 固定密码。实测原因：移除卷后 Aspire 的建库脚本（`docker exec psql`）与 postgres 首次初始化存在竞态，psql 挂起导致 `StartAsync` 卡死至超时；保留卷 + 固定密码使数据目录预热且密码跨轮一致（同时消除旧卷密码不匹配问题）。
- **typesense 例外**：镜像内无 `/data` 目录（数据目录依赖挂载创建），移除卷后补匿名卷挂载，否则 typesense 30.1 拒绝启动。

### 资源访问

- `app.CreateHttpClient("gateway")` 走 YARP（路由：`/activities` `/tags` `/lives` `/search`，见 AppHost.cs）。
- `/account/*` 不在网关路由内，须直连 `app.CreateHttpClient("account-svc")`。
- typesense / keycloak 的 base URL 用 `app.GetEndpoint(...)`。

## 5. Keycloak 测试 realm（KeycloakTestRealm）

在 AppHost healthy 后执行一次（幂等：先删后建）。实测要点：

1. Admin token：master realm `admin-cli` 密码授权。凭据优先从 `KeycloakResource` 模型读取（`AdminUserNameParameter`/`AdminPasswordParameter`），读取失败回退固定值（模型参数在测试构建下可能抛 NRE）。
2. DELETE realm `makerverse`（404 忽略）→ POST 重建（`enabled:true, sslRequired:"none"`）。
3. client `makerverse`（public、directAccessGrantsEnabled）+ **audience mapper**：实测 token 默认 `aud="account"`，而服务端校验 `Audience="makerverse"`（Common/AuthExtensions.cs），无 mapper 则全部 401。
4. client `makerverse-account-service`（confidential、secret 与参数一致）+ **service account 角色**（realm-management 的 manage-users/view-users）：AccountService 以 client_credentials 调 Admin API 创建/查询用户，无角色则注册返回 403。
5. role `Admin`——**大小写敏感**：TagsController 用 `[Authorize(Roles = "Admin")]`，.NET `IsInRole` 是区分大小写的 ordinal 比较，realm 角色名必须为 `"Admin"`（实测小写 `"admin"` 恒 403）。
6. users `testuser` / `testadmin`（密码 test-password），`testadmin` 授予 `Admin` 角色。角色映射用 **POST** `role-mappings/realm`：Keycloak 26 该端点只有 GET/POST/DELETE（实测 PUT 405）。
7. realm-role mapper → token 顶层 `roles` claim：JwtBearer 默认 `MapInboundClaims` 会将其映射为 `ClaimTypes.Role`，`[Authorize(Roles)]` 依赖它。

固定凭据（TestConstants.cs）：

| 主体 | 凭据 |
|---|---|
| Keycloak admin | `admin` / `test-admin-password` |
| testuser（普通） | `testuser` / `test-password` |
| testadmin（Admin 角色） | `testadmin` / `test-password` |
| client `makerverse` | public，directAccessGrants |
| client `makerverse-account-service` | secret = `test-account-service-secret` |

## 6. 用例清单

### SmokeTests

| 用例 | 断言 |
|---|---|
| S1 网关可达 | gateway `GET /activities` → 200 |
| S2 全栈健康 | 依次 `WaitForResourceHealthyAsync`：account-svc / activity-svc / live-svc / search-svc / gateway / keycloak / postgres / redis / **messaging** / minio / typesense / livestream-svc |
| S3 健康端点 | activity-svc 直连 `GET /health` → 200 |

> S2 资源名：RabbitMQ 的实际资源名为 `messaging`（AppHost.cs），非 `rabbitmq`。

### AuthFlowTests

| 用例 | 断言 |
|---|---|
| A1 注册→登录→me | register 200（`Ok(...)`）；token 200 含 access_token；带 Bearer `GET me` 200 且 id/username 与注册一致 |
| A2 更新资料 | `PUT me` 200；再 `GET me` 字段已更新 |
| A3 未授权访问 | 无 token `GET me` → 401 |
| A4 头像上传与读取 | multipart PNG → 200；匿名 `GET /account/users/{id}/avatar` → 200 + `image/png` |
| A5 超限拒绝 | >5MB → 400（实测：`AvatarOptions.MaxFileSizeBytes` 校验走 Error.Validation，非 413） |
| A6 token 刷新 | refresh → 200 + 新 access_token |

### ActivityFlowTests

| 用例 | 断言 |
|---|---|
| C1 创建活动 | 登录 POST `/activities`（tag 需先由 admin 创建）→ 201 + id |
| C2 浏览量递增 | 连续两次 GET，第二次 viewCount ≥ 第一次（单调，不断言精确值） |
| C3 非法 slug 拒绝 | tags 含 `Bad Slug!` → 400（TagSlugValidator：3–50 位小写字母/数字/连字符） |
| C4 评论 | POST comments → 201；`GET /activities/{id}/comments` 含该评论 |
| C5 标签重复 409 | POST `/tags` 唯一 slug → 201；重复 → 409；GET `/tags` 含该 slug |
| C6 标签需 admin | testuser → 403；testadmin → 201 |
| C7 Redis 缓存 | 连续两次 GET `/tags` 响应体一致（不做内部状态断言） |

### LiveFlowTests

| 用例 | 断言 |
|---|---|
| L1 创建直播 | 登录 POST `/lives` → 201 + id |
| L2 在线列表 | `GET /lives/online` → 200 + JSON 数组（是否含新建直播按实现语义，断言仅结构与状态码） |
| L3 端点获取 | `PUT status {"status":"start"}` → 200；`GET /lives/{id}/endpoint` → 200，ingestUrl 含 liveId；非创建者 → 200 且 ingestUrl 为 null |
| L4 HLS 未就绪 400 | status=Created 时 `GET /lives/{id}/segments/index.m3u8` → 400 |
| L5 删除直播 | start 后等待 Stopped（precreate TTL 30s + watcher 置位）→ DELETE → 204 → GET → 404 |

> 实测语义（LiveService 控制器）：endpoint 仅对 Starting/Started 状态返回 200（Created → 404）；DELETE 仅允许 Stopped/Invalid（Created → 400）；HLS 路由为 `/lives/{id}/segments/index.m3u8`。

### SearchFlowTests（Wolverine/RabbitMQ 异步，全部轮询断言 ≤30s）

| 用例 | 断言 |
|---|---|
| M1 创建→索引 | 标题含唯一词 → 轮询 gateway `/search/activities?query=…` 出现 |
| M2 更新→重索引 | PUT 改标题 → 轮询新词可搜到 |
| M3 删除→消失 | DELETE → 轮询旧词不再返回该 id（含空数组） |
| M4 直查 Typesense | `X-TYPESENSE-API-KEY` 直查 `collections/activities/documents/search` → 200 + 文档 |
| M5 直播索引 | 创建直播 → 轮询 `/search/lives?query=…` 出现 |
| M6 标签过滤 | `query=词 [tag-slug]` → 命中且 tags 含该 slug |

> 实测：SearchController 参数名为 `query`（非 `q`）；标签过滤是 query 内 `[tag]` 语法（非独立参数）。

### UnitTests（Tier 1）

| 用例 | 断言 |
|---|---|
| U1 TagSlugValidatorAttribute | `abc-123` 合法；`Abc`/`a`/`bad slug`/`带中文` 非法 |
| U2 UpdateStatusValidatorAttribute | `start`/`stop` 合法；`paused`/空 非法 |
| U3 ErrorExtensions 映射 | NotFound→404、Conflict→409、Forbidden→403、Unauthorized→401、Validation→400 |

## 7. 实测注意事项

- **SearchService 消息丢失修复**（已随测试提交）：Wolverine 6 默认 `ServiceLocationPolicy.NotAllowed` + `AddTypesenseClient` 的 opaque lambda 注册 → handler 动态代码生成失败 → 消息被**静默丢弃**（无日志、不进死信）。SearchService/Program.cs 已设置 `AllowedButWarn`。无此修复时全部 Search 用例超时。
- 首次运行：拉取 postgres/rabbitmq/redis/minio/typesense/keycloak/yarp 镜像 + 本地构建 livestream-svc 镜像（预算 5–10 分钟），后续复用镜像与 NuGet 缓存。
- Keycloak 冷启动 30–60s；无真实推流时直播状态经 precreate TTL（默认 30s）由 watcher 置为 Stopped（L5 依赖此行为）。
- RabbitMQ 异步无强一致 → 消息断言一律轮询 + 超时；viewCount 递增非幂等 → 断言单调关系；Typesense 更新非幂等 → 用例数据唯一化。
- 测试进程异常退出时 DCP 可能残留容器，可手动清理：`podman rm -f $(podman ps -q)`。
- 本机环境变量 `CI=1` 时按 CI 超时（5 分钟）运行。

## 8. 未实现部分

- **CI 方案**（设计给出，未落地）：`.github/workflows/integration-tests.yml`：

```yaml
name: Integration Tests
on:
  push: { branches: [main] }
  pull_request: { branches: [main] }
jobs:
  test:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Run integration tests
        run: dotnet test --no-build --verbosity normal
```

  要点：Linux runner（容器运行时必需）；随机端口默认开；`CI=true` 自动延长超时；livestream-svc 镜像构建慢，可用 `docker/build-push-action` cache 或 `actions/cache` 优化。

- **Tier 2 服务级集成**（可选）：ActivityService 用 Testcontainers（PostgreSQL + Redis + RabbitMQ）起单服务验证 controller 与消息发布；LiveService 需 mock gRPC（livestream-svc），实现成本高，优先级最低。
