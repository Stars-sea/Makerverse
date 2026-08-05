# Makerverse

直播平台后端：基于 **.NET Aspire** 的微服务集群（认证 / 活动 / 直播 / 搜索）+ **Rust** 直播媒体服务（livestream-rs，RTMP/RTSP 接入、HTTP-FLV 分发、HLS 持久化）。

## 架构

Aspire AppHost（`Makerverse.AppHost`）编排全部服务与基础设施容器，管理连接串与启动顺序（`.WaitFor()`）。

| 服务 | 职责 | 消息 |
|---|---|---|
| **AccountService** | 认证（Keycloak OIDC 密码授权、token 刷新、登出）、用户 CRUD（Keycloak Admin API）、头像上传/读取（MinIO） | 无 |
| **ActivityService** | 博客式活动：标签、评论、投票；标签缓存于 Redis | 发布 `ActivityCreated/Updated/Deleted` 到 `activities` 交换机 |
| **LiveService** | 直播会话 CRUD、HLS 段服务（MinIO 读取）、`LivestreamLifecycleWatcher`（gRPC 流式监听会话状态） | 发布 `LiveCreated/Updated/Deleted/Connected/Terminate` 到 `lives` 交换机 |
| **SearchService** | Typesense 全文检索；消费活动/直播事件并索引 | 消费 `activities.search` / `lives.search` 队列 |
| **livestream-rs**（Rust 子模块） | SRT/RTMP 接入、RTSP（含 MJPEG→H.264 服务端转码）、HTTP-FLV 播放、TS 段上传 MinIO、gRPC 控制面 | — |

基础设施（AppHost 托管）：PostgreSQL（keycloak/activity/live 三库）、Keycloak、RabbitMQ、Redis、MinIO、Typesense、YARP 网关、nginx-proxy（仅 production）。

跨服务通信：**Wolverine + RabbitMQ**（契约见 `Contracts`，事件驱动；SearchService 为消费者）。服务间不共享数据库。

## 快速开始

前置：.NET SDK 10、Aspire CLI 13.4.6、Docker（podman 亦可）、Rust 工具链 + FFmpeg 开发库（仅 livestream-rs）。

```bash
# 本地起全栈（Aspire 编排所有服务 + 基础设施）
aspire run

# AppHost 必需的用户密钥
dotnet user-secrets --project Makerverse.AppHost set "account-service-client-secret" "<value>"
dotnet user-secrets --project Makerverse.AppHost set "typesense-api-key" "<value>"

# Rust 子模块
cd livestream-rs && cargo build
```

## 测试

- **Tier 1 单元**：`Makerverse.AppHost.Tests/UnitTests`（校验器、错误映射）。
- **Tier 3 Aspire E2E**：`Makerverse.AppHost.Tests`（共享 AppHost fixture 的全栈集成测试：认证/活动/直播/搜索流）。
- **压力测试**：`LivestreamStressTests`（`Category=Stress`）经 gRPC 控制面创建并发直播，ffmpeg 推流（RTMP）并验证拉流收帧；真实宿主媒体端口经 `--rtmp-port/--rtsp-port/--http-flv-port` 传入（测试模式端口随机化）。
- **livestream-rs**：单元 + 集成测试（`crates/*/tests/`）与 `scripts/e2e-test.sh`（RTMP/RTSP → HTTP-FLV）。
- **CI**：`.github/workflows/integration-tests.yml` 跑全套（含 Stress；需 cargo/ffmpeg）；子模块另有自己的 CI。

```bash
dotnet test                              # 仓库根；首次运行拉镜像 + 构建 livestream-svc，预算 5–10 分钟
dotnet test --filter "Category=Stress"   # 仅压力测试
dotnet test --filter SmokeTests          # 仅冒烟
```

## 仓库结构

```
Makerverse.AppHost/           # Aspire 编排（服务、基础设施、参数、生产 compose）
Makerverse.AppHost.Tests/     # 单元 + E2E + 压力测试（README 见其目录）
AccountService/ ActivityService/ LiveService/ SearchService/   # 业务服务
Common/                       # 跨服务扩展（认证、CORS、错误映射、Wolverine/RabbitMQ）
Contracts/                    # 消息契约 DTO（零依赖）
Makerverse.ServiceDefaults/   # OTel、服务发现、健康检查、HTTP 韧性
livestream-rs/                # Rust 直播媒体服务（子模块，独立仓库）
.github/workflows/            # CI
```

## 相关文档

- [测试套件说明](Makerverse.AppHost.Tests/README.md)（用例清单、fixture 设计、实测注意事项）
- [livestream-rs](livestream-rs/README.md) 及 `livestream-rs/docs/`（管道/数据流架构）
