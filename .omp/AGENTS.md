# AGENTS.md

This file provides project context when working with code in this repository.

## Build & Run

```bash
# Run the entire distributed system locally (Aspire orchestrator starts all services + infrastructure)
aspire run

# Build all .NET projects
dotnet build

# Manage user secrets (required for the AppHost)
dotnet user-secrets --project Makerverse.AppHost set "account-service-client-secret" "<value>"
dotnet user-secrets --project Makerverse.AppHost set "typesense-api-key" "<value>"

# Build the Rust livestream submodule (requires: Rust toolchain, FFmpeg dev libs, protobuf-compiler)
cd livestream-rs && cargo build
```

There are no **.NET test projects** in this repository. The Rust submodule (`livestream-rs`) has unit tests (`#[test]`), integration tests in `tests/`, and an automated E2E test (see Rust section below). Database migrations (EF Core) are applied automatically at startup in each service's `Program.cs` via `context.Database.MigrateAsync()`.

## Architecture

This is a **.NET Aspire distributed application** (microservices) targeting `net10.0`. The `Makerverse.AppHost` project is the orchestrator — it defines all services and infrastructure containers, wires connection strings, and manages startup order via `.WaitFor()`.

### Services

| Service | Responsibility | Messaging |
|---|---|---|
| **AccountService** | Auth (Keycloak OIDC password grant, token refresh, logout), user CRUD via Keycloak Admin API, avatar upload/serve via MinIO | None |
| **ActivityService** | Blog-like activities with tags, comments, voting. Tags cached in Redis. | Publishes `ActivityCreated/Updated/Deleted` to `activities` exchange |
| **LiveService** | Live stream session CRUD, HLS segment serving from MinIO, gRPC client to `livestream-rs`. `LivestreamLifecycleWatcher` (BackgroundService) watches stream status via gRPC streaming. | Publishes `LiveCreated/Updated/Deleted/Connected/Terminate` to `lives` exchange |
| **SearchService** | Full-text search via Typesense. Listens to RabbitMQ for activity/live events and indexes them. | Consumes from `activities.search` and `lives.search` queues |
| **livestream-rs** (Rust submodule) | Live media ingest (SRT/RTMP), RTMP egress, HTTP-FLV playback. TS segment upload to MinIO. gRPC control plane. | N/A |

### Infrastructure (all managed by AppHost)

PostgreSQL (3 databases: `keycloak-db`, `activity-db`, `live-db`), Keycloak, RabbitMQ, Redis, MinIO, Typesense, YARP gateway, nginx-proxy (production only).

### Shared Libraries

- **Common** — Auth configuration (`AuthExtensions.AddKeycloakAuthentication`), CORS (`CorsExtensions.AddTauriCors` — allows `tauri://localhost`, `https://tauri.localhost`, `http://tauri.localhost`), error handling (`ErrorExtensions.ToActionResult` — converts `ErrorOr` errors to HTTP responses), Wolverine/RabbitMQ setup (`WolverineExtensions.UseWolverineWithRabbitMqAsync` with retry policies)
- **Contracts** — Message contract DTOs (plain POCOs, no dependencies): `ActivityCreated/Updated/Deleted`, `LiveCreated/Updated/Deleted/Connected/Terminate`
- **Makerverse.ServiceDefaults** — Aspire shared defaults: OpenTelemetry (metrics, tracing, OTLP export), service discovery, HTTP resilience, health checks at `/health` and `/alive` (dev only)

### Messaging (Wolverine + RabbitMQ)

Services use **WolverineFx** over RabbitMQ. Each publishing service calls `UseWolverineWithRabbitMqAsync` to configure the RabbitMQ transport and declares its exchange via `PublishAllMessages().ToRabbitExchange(...)`. Consumers (SearchService) use Wolverine message handlers — any class with a `HandleAsync` method matching the message type, discovered by convention. Message contracts live in the `Contracts` project.

### Authentication

Keycloak is the identity provider (realm: `makerverse`). AccountService handles login via OIDC password grant and user management via Keycloak Admin REST API (client credentials). All other services validate JWT bearer tokens using `Aspire.Keycloak.Authentication`. If Keycloak is unreachable at startup, auth is gracefully disabled with a warning log.

### Error Handling

The codebase uses the **ErrorOr** library for the Result pattern. Controllers return `IActionResult`; errors are converted via the `ErrorExtensions.ToActionResult()` helper in Common. Do not throw exceptions for domain errors — return `ErrorOr<T>` instead.

### Key Conventions

- Each service uses **Controllers** (not Minimal APIs), mapped via `app.MapControllers()`
- Services register with `builder.AddServiceDefaults()` for OpenTelemetry, health checks, service discovery, and HTTP resilience
- EF Core `DbContext` is registered via `builder.AddNpgsqlDbContext<T>("connection-name")` (Aspire integration)
- Infrastructure clients use Aspire integrations: `builder.AddRedisClient("redis")`, `builder.AddMinioClient("minio")`
- CORS, auth, and Wolverine setup use extension methods from the `Common` project — never inline the configuration
- The AppHost uses `.WithContainerRegistry(registry).WithRemoteImageTag("latest")` on every project resource, pointing to a configurable container registry

## Design Principles

- **High cohesion, low coupling.** Each service is an independent bounded context with its own database. Services communicate only via Wolverine/RabbitMQ messages; never share databases or call another service's internals directly.
- **Separation of concerns.** Cross-cutting concerns (auth, CORS, OpenTelemetry, resilience) live in `Common` and `ServiceDefaults` as extension methods — keep them out of business logic.
- **Contract-first.** Message DTOs go in the zero-dependency `Contracts` project. When adding an event: define the DTO → declare the exchange in the publisher's `Program.cs` → add a consumer in SearchService.
- **Result pattern over exceptions.** Use `ErrorOr<T>` for domain errors; convert to HTTP responses via `ErrorExtensions.ToActionResult()`. Exceptions only for infrastructure failures.
- **Infrastructure as configuration.** All dependencies are defined in `AppHost.cs` and injected via `.WithReference()`. Services use Aspire integrations (`AddNpgsqlDbContext`, `AddRedisClient`, etc.) — never hardcode connection strings.

## Rust (livestream-rs)

- The project uses **tokio** (multi-threaded runtime) with **tonic** for gRPC and **axum** for HTTP-FLV. All I/O is async — never introduce blocking calls in async contexts.
- The architecture separates **transport** (connection/session management, in `transport/`) from **pipeline** (media processing middleware chain, in `pipeline/`), coordinated via events and bounded channels. Keep this boundary clean.
- Configuration uses `config.toml` + environment variables (`__` for nesting). MinIO connection is mandatory at startup.
- gRPC proto definitions live in `proto/livestream.proto`. After changing them, rebuild with `cargo build` (the `build.rs` runs `tonic-prost-build`).
- Key docs live in `docs/` — read documents before making architectural changes.

### Testing

- **Unit tests**: each crate has `#[cfg(test)]` modules with `#[test]` / `#[tokio::test]` covering codecs, pads, FLV encoding, RTP parsing, FFmpeg wrappers.
- **Integration tests**: `crates/livestream-transport/tests/` (session lifecycle) and `crates/livestream-pipeline/tests/` (pipeline assembly, spy-based FLV verification).
- **E2E test**: `scripts/e2e-test.sh` builds the server and test-client, starts `livestream`, pushes a test video via ffmpeg, and verifies RTMP/RTSP pull receives video frames. Runs fully automated in <40s.

```bash
# Automated E2E (CI-ready)
./scripts/e2e-test.sh

# Automated via cargo (server must already be running)
cargo run --release -p test-client -- --auto --duration 10 testdata/sample.mp4

# Manual interactive (original mode, unchanged)
cargo run -p test-client -- testdata/sample.mp4
```
