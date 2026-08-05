# Makerverse

A live-streaming backend platform: a .NET Aspire microservice cluster (auth / activities / live / search) plus a Rust media server (`livestream-rs`) for RTMP/RTSP ingest, HTTP-FLV delivery, and HLS persistence.

## Features

- **Streaming ingest** — RTMP and RTSP (with server-side MJPEG → H.264 transcoding) via `livestream-rs`
- **Delivery** — HTTP-FLV playback, HLS segment upload to MinIO
- **Auth** — Keycloak-backed OIDC password flow, token refresh, and user management (AccountService)
- **Live sessions** — session lifecycle CRUD, watcher-driven state, HLS segment serving (LiveService)
- **Search** — Typesense full-text search over activities and lives (SearchService)
- **Event-driven** — Wolverine + RabbitMQ messaging across services

## Architecture

The Aspire AppHost (`Makerverse.AppHost`) orchestrates all services and infrastructure containers, wiring connection strings and startup order (`.WaitFor()`).

| Service | Responsibility | Messaging |
|---|---|---|
| **AccountService** | Auth (Keycloak OIDC password grant, token refresh, logout), user CRUD (Keycloak Admin API), avatar upload/read (MinIO) | — |
| **ActivityService** | Blog-style activities: tags, comments, votes; tag cache in Redis | Publishes `ActivityCreated/Updated/Deleted` to the `activities` exchange |
| **LiveService** | Live session CRUD, HLS segment serving (MinIO), `LivestreamLifecycleWatcher` (gRPC stream of session state) | Publishes `LiveCreated/Updated/Deleted/Connected/Terminate` to the `lives` exchange |
| **SearchService** | Typesense full-text search; consumes activity/live events and indexes them | Consumes `activities.search` / `lives.search` queues |
| **livestream-rs** (Rust submodule) | SRT/RTMP ingest, RTSP (incl. MJPEG → H.264 server-side transcode), HTTP-FLV playback, TS segment upload to MinIO, gRPC control plane | — |

Managed infrastructure (AppHost-hosted): PostgreSQL (keycloak/activity/live databases), Keycloak, RabbitMQ, Redis, MinIO, Typesense, a YARP gateway, and nginx-proxy (production only).

Cross-service communication: **Wolverine + RabbitMQ** (contracts in `Contracts`; SearchService is the consumer). Services do not share databases.

## Getting Started

### Prerequisites

- .NET SDK 10
- Aspire CLI 13.4.6
- Docker (podman also works)
- Rust toolchain + FFmpeg dev libraries (livestream-rs only)

### Quick start

```bash
# Run the full stack locally (Aspire orchestrates all services + infrastructure)
aspire run

# AppHost secrets
dotnet user-secrets --project Makerverse.AppHost set "account-service-client-secret" "<value>"
dotnet user-secrets --project Makerverse.AppHost set "typesense-api-key" "<value>"

# Rust submodule
cd livestream-rs && cargo build
```

## Configuration

### Transcode settings (RTSP MJPEG → H.264)

`livestream-rs` transcodes RTSP MJPEG sources server-side. Parameters are read from environment variables (`livestream-core` `TranscodeConfig`, `__` as the nesting separator; see `crates/livestream-core/src/config.rs`):

| Environment variable       | Default | Description                    |
| -------------------------- | ------- | ------------------------------ |
| `TRANSCODE__BITRATE_KBPS`  | 4096    | Target bitrate (kbps)          |
| `TRANSCODE__PRESET`        | medium  | x264 preset                    |
| `TRANSCODE__GOP_SECS`      | 2.0     | Keyframe interval (seconds)    |
| `TRANSCODE__FPS`           | (unset) | Output frame rate; unset = follow source |

Two ways to inject:

1. Container environment variables — via compose `environment:` or AppHost `WithEnvironment("TRANSCODE__BITRATE_KBPS", "4096")`.
2. Typed AppHost configuration (recommended, `Makerverse.AppHost/AppHost.cs`):

   ```csharp
   .WithTranscodeConfig(config => config
       .WithBitrate(4096)
       .WithPreset(TranscodePreset.Medium))
   ```

> **Gotcha: the encoder must set an explicit frame rate.** x264 derives its rate-control frame rate from `framerate`, falling back to `time_base`. Setting only `time_base = 1/1000` (millisecond PTS) makes x264 assume a 1000 fps source, diluting the per-frame bit budget 1000× — output QP stays pinned near 51 and the FLV stream runs at tens of kb/s regardless of `TRANSCODE__BITRATE_KBPS`. Fixed in `livestream-rs` `transcode.rs` `create_encoder` (explicit `framerate`, plus calibration against the measured source frame rate when `cfg.fps` is unset).

## Deployment

```bash
# Build/push images and generate compose artifacts
aspire deploy

# Apply the stack manually — the deploy's compose-up step hangs with
# podman-compose on this host, so compose is applied by hand
cd Makerverse.AppHost/aspire-output && \
  docker compose -p "<project-name>" --env-file .env.Production -f docker-compose.yaml up -d

# Verify routing (HTTP 400 = routing OK; 503 = stack not up)
curl -X POST -H 'Content-Type: application/json' -d '{}' http://id.makerverse.local/account/auth/token
```

`<project-name>` = `DockerCompose:production:ProjectName` in `~/.aspire/deployments/*/production.json` (e.g. `aspire-production-<hash>`). **Keep it stable across deploys** — a different name starts a second stack (port conflicts) with a fresh Keycloak, so re-register users via `POST /account/users/register` if they're missing after a project change.

## Testing

- **Tier 1 unit tests**: `Makerverse.AppHost.Tests/UnitTests` (validators, error mapping).
- **Tier 3 Aspire E2E**: `Makerverse.AppHost.Tests` (full-stack integration against a shared AppHost fixture: auth / activity / live / search flows).
- **Stress tests**: `LivestreamStressTests` (`Category=Stress`) create concurrent lives via the gRPC control plane, push with ffmpeg (RTMP), and verify frame receipt; real host media ports are passed via `--rtmp-port` / `--rtsp-port` / `--http-flv-port` (test-mode ports are randomized).
- **livestream-rs**: unit + integration tests (`crates/*/tests/`) plus `scripts/e2e-test.sh` (RTMP/RTSP → HTTP-FLV).
- **CI**: `.github/workflows/integration-tests.yml` runs the full suite (including Stress; requires cargo/ffmpeg); the submodule has its own CI.

```bash
dotnet test                              # From the repo root; first run pulls images + builds livestream-svc, budget 5–10 min
dotnet test --filter "Category=Stress"   # Stress tests only
dotnet test --filter SmokeTests          # Smoke tests only
```

## Repository Layout

```
Makerverse.AppHost/           # Aspire orchestration (services, infrastructure, parameters, production compose)
Makerverse.AppHost.Tests/     # Unit + E2E + stress tests (see its README)
AccountService/ ActivityService/ LiveService/ SearchService/   # Business services
Common/                       # Cross-service extensions (auth, CORS, error mapping, Wolverine/RabbitMQ)
Contracts/                    # Message contract DTOs (zero dependencies)
Makerverse.ServiceDefaults/   # OTel, service discovery, health checks, HTTP resilience
livestream-rs/                # Rust media server (submodule, separate repository)
.github/workflows/            # CI
```

## Documentation

- [Test suite guide](Makerverse.AppHost.Tests/README.md) (case list, fixture design, practical notes)
- [livestream-rs](livestream-rs/README.md) and `livestream-rs/docs/` (pipeline/data-flow architecture)
