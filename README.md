# Nevma

Voice-first planning and collaboration platform.

## Services

- `Nevma.Gateway`: the single mobile API entry point and reverse proxy.
- `Nevma.Identity.Api`: users, devices, sessions, contacts, and permissions.
- `Nevma.Planning.Api`: calendar, meeting invitations, tasks, and reminders.
- `Nevma.Messaging.Api`: conversations and SignalR realtime messaging.
- `Nevma.Notifications.Api`: notification inbox, protected push devices, and delivery workers.
- `Nevma.Files.Api`: private file storage, validation, scanning, and access grants.
- `Nevma.Commands.Api`: voice/text command previews, confirmation, execution, audit, and undo.
- `Nevma.Contracts`: versioned integration contracts shared between services.
- `Nevma.ServiceDefaults`: shared HTTP error handling, correlation IDs, security headers,
  health checks, and reusable result primitives. It contains no business rules.

The authenticated Gateway endpoint `GET /api/home` composes the mobile Home view from service
APIs in parallel. It returns the next task, urgent tasks, upcoming calendar events, pending
connections, and unread notifications without reading another service's database.

`GET /api/search?q=...` performs an authenticated, partial-failure-tolerant search across the
caller's planning items, conversations/messages, and accessible files. Each source applies its
own ownership or participant rules before the Gateway merges and sorts results.

Task owners can share a task with another user as either a viewer or an editor. Shared tasks are
returned by `GET /api/tasks/shared` and appear in global search, while only owners can manage
collaborators or delete the task. Editors can update and complete it; viewers have read-only access.

## Local development

The repository is pinned to .NET SDK 10.0.301.

Docker Desktop provides PostgreSQL, RabbitMQ, Redis, and Jaeger for local development. The setup
script creates an ignored `.env` with random credentials, starts the containers, builds the
solution, and applies all six service migrations:

```powershell
.\scripts\setup-local.ps1
.\scripts\start-local.ps1 -SkipSetup
```

The Gateway is then available at `http://localhost:5033`. RabbitMQ management is at
`http://localhost:15672` and Jaeger tracing is at `http://localhost:16686`. The RabbitMQ username
and password are stored in the local `.env` file. Stop the APIs, or the APIs and containers, with:

```powershell
.\scripts\stop-local.ps1
.\scripts\stop-local.ps1 -Infrastructure
```

The OpenTelemetry Collector receives local OTLP traffic on ports `4317` and `4318` and forwards
traces to Jaeger. Start the optional Prometheus and Grafana stack with:

```powershell
docker compose --profile observability up -d
```

Prometheus is available at `http://localhost:9090` and Grafana at `http://localhost:3000`.
Grafana is preconfigured with the Nevma backend dashboard, Prometheus alerts, and Jaeger traces.
Set `GRAFANA_ADMIN_PASSWORD` in `.env`; the local fallback password must never be used in production.

Docker ports bind to `127.0.0.1` only. Messaging uses Redis for the authenticated SignalR
backplane and shared presence state; PostgreSQL remains the source of truth for conversations
and messages. Connected clients should invoke the `Heartbeat` hub method at least once per minute.

Run the live first-milestone test against the started backend with:

```powershell
$env:NEVMA_RUN_E2E="true"
dotnet test tests/Nevma.EndToEndTests
```

It exercises registration, OAuth authorization code with PKCE, contacts, meeting acceptance,
calendar persistence, RabbitMQ notifications, conversation integration, and task sharing through
a confirmed voice command followed by undo. The test is skipped in the normal unit-test run when
`NEVMA_RUN_E2E` is not enabled.

The CI workflow also runs a short k6 health-load gate. Run an authenticated staging load test with:

```powershell
$env:ACCESS_TOKEN="<short-lived-test-token>"
$env:GATEWAY_URL="https://staging-api.nevma.example.com"
docker run --rm -e ACCESS_TOKEN -e GATEWAY_URL `
  -v "${PWD}/tests/load:/scripts:ro" grafana/k6:2.0.0 `
  run /scripts/authenticated-journey.js
```

```powershell
dotnet restore Nevma.slnx
dotnet build Nevma.slnx
dotnet test Nevma.slnx
```

Run the APIs in separate terminals:

```powershell
dotnet run --project src/Services/Identity/Nevma.Identity.Api
dotnet run --project src/Services/Planning/Nevma.Planning.Api
dotnet run --project src/Services/Messaging/Nevma.Messaging.Api
dotnet run --project src/Services/Notifications/Nevma.Notifications.Api
dotnet run --project src/Services/Files/Nevma.Files.Api
dotnet run --project src/Services/Commands/Nevma.Commands.Api
dotnet run --project src/Gateway/Nevma.Gateway
```

The Identity service now owns a PostgreSQL schema named `identity`. Supply its password and
other environment-specific values outside source control, for example:

```powershell
$env:ConnectionStrings__IdentityDatabase="Host=localhost;Port=5432;Database=nevma_identity;Username=nevma_identity;Password=<secret>"
dotnet ef database update --project src/Services/Identity/Nevma.Identity.Api
```

Identity uses ASP.NET Core Identity for password hashing and OpenIddict for OAuth 2.0/OpenID
Connect. The mobile client uses authorization code flow with PKCE through `/connect/authorize`
and `/connect/token`; there is no endpoint that directly turns a password into an access token.
Development seeds the public `nevma-mobile` client. Non-development environments must provide
signing and encryption PFX certificates through the `Authentication__*CertificatePath` and
`Authentication__*CertificatePassword` configuration keys.

External Google and Facebook sign-in challenges are available at `/identity/account/external/google`
and `/identity/account/external/facebook`. They remain disabled until their client credentials are
provided through `Authentication__Google__*` and `Authentication__Facebook__*` environment variables.
The callback signs the user into the existing Identity cookie and returns to the OpenIddict flow;
provider secrets never belong in the frontend. Instagram requires Meta's separate Instagram Login
adapter and is intentionally not treated as a Facebook alias.

Mobile authorization requests should include `device_id`, `device_name`, and `device_platform`.
Identity binds the resulting access and refresh tokens to a durable device session. Authenticated
clients can list sessions at `GET /api/auth/sessions` and revoke one with
`DELETE /api/auth/sessions/{id}`; revocation invalidates every token for that authorization.

Authenticated users can configure TOTP MFA under `/api/auth/mfa`, regenerate one-time recovery
codes, and inspect recent sign-in/security activity at `/api/auth/security-events`. Enabling or
disabling MFA revokes existing authorizations. Uploaded images are malware-scanned, dimension
checked, decoded, and re-encoded before storage so embedded metadata is not retained.

Password reset and email-confirmation endpoints use ASP.NET Core Identity's protected,
single-purpose tokens. SMTP delivery is disabled locally. Enable `EmailDelivery__Enabled` only
after setting `EmailDelivery__SmtpHost`, `EmailDelivery__FromAddress`, and credentials through a
secret provider. Recovery endpoints never disclose whether an email address is registered and do
not log recovery tokens.

The Planning service owns a separate PostgreSQL database/schema and validates `nevma_api`
access tokens issued by Identity. Configure and migrate it independently:

```powershell
$env:ConnectionStrings__PlanningDatabase="Host=localhost;Port=5432;Database=nevma_planning;Username=nevma_planning;Password=<secret>"
$env:Authentication__Issuer="https://identity.example.com/"
dotnet ef database update --project src/Services/Planning/Nevma.Planning.Api
```

The Messaging service owns the `messaging` schema and validates the same Identity access
tokens. Messages are committed to PostgreSQL before SignalR publishes them. Configure and
migrate this database independently as well:

```powershell
$env:ConnectionStrings__MessagingDatabase="Host=localhost;Port=5432;Database=nevma_messaging;Username=nevma_messaging;Password=<secret>"
$env:Authentication__Issuer="https://identity.example.com/"
dotnet ef database update --project src/Services/Messaging/Nevma.Messaging.Api
```

Notifications owns another isolated PostgreSQL schema. Push tokens are protected with ASP.NET
Core Data Protection and are never returned by the API:

```powershell
$env:ConnectionStrings__NotificationsDatabase="Host=localhost;Port=5432;Database=nevma_notifications;Username=nevma_notifications;Password=<secret>"
$env:Authentication__Issuer="https://identity.example.com/"
dotnet ef database update --project src/Services/Notifications/Nevma.Notifications.Api
```

Production deployments must persist the Data Protection key ring in a shared protected store;
losing or changing that key ring makes existing protected push tokens unreadable.

FCM delivery is disabled by default. Enable it only after configuring Application Default
Credentials with the minimum Firebase Cloud Messaging role and setting the target project:

```powershell
$env:GOOGLE_APPLICATION_CREDENTIALS="C:\secure\nevma-fcm-service-account.json"
$env:PushDelivery__Enabled="true"
$env:PushDelivery__ProjectId="<firebase-project-id>"
```

The credential file must stay outside the repository. Delivery attempts are persisted before
sending, retry transient failures with bounded backoff, and revoke devices after permanent
provider errors such as an unregistered target.

Redis SignalR scale-out remains a later infrastructure slice; PostgreSQL is the source of truth
for conversations and messages.

Files and Commands own the `files` and `commands` schemas respectively. Their credentials must
also be supplied outside source control before applying migrations:

```powershell
$env:ConnectionStrings__FilesDatabase="Host=localhost;Database=nevma_files;Username=nevma_files;Password=<secret>"
$env:ConnectionStrings__CommandsDatabase="Host=localhost;Database=nevma_commands;Username=nevma_commands;Password=<secret>"
dotnet ef database update --project src/Services/Files/Nevma.Files.Api
dotnet ef database update --project src/Services/Commands/Nevma.Commands.Api
```

The default file adapter stores content under the service's private `App_Data/files` directory.
Production can select the built-in S3-compatible adapter (AWS S3, MinIO, or another compatible
private store) and ClamAV streaming scanner without code changes. Uploads are capped at 25 MB and
validated by extension, MIME type, file signature, SHA-256, authorization, and malware scan. The
scanner fails closed when ClamAV is unavailable. S3 downloads can use authorization-checked signed
URLs that expire after at most 15 minutes; local storage continues to stream through the API.

```powershell
$env:FileStorage__Provider="S3"
$env:FileStorage__BucketName="nevma-private-files"
$env:FileStorage__Region="eu-central-1"
$env:FileStorage__ServiceUrl="https://s3.example.com" # optional for AWS
$env:FileStorage__AccessKey="<secret>"
$env:FileStorage__SecretKey="<secret>"
$env:MalwareScanning__Provider="ClamAV"
$env:MalwareScanning__Host="clamav"
```

Notification events are persisted before delivery. They are published immediately to authenticated
clients on `/hubs/notifications`, with Redis scale-out when enabled, while privacy-safe Firebase push
attempts remain durable and retryable in PostgreSQL.

Commands never execute directly from raw voice text. `/api/commands/preview` creates a durable
structured plan, `/confirm` executes it with the authenticated user's token, and `/undo` reverses
supported operations. Raw transcripts are not persisted; the audit record stores their hash. The
rule parser supports task creation/completion/deletion, meeting creation/acceptance/decline/
cancellation, task sharing, and conversation messages in English or Greek command prefixes. The
deterministic sharing syntax is `share task: <task-id> | <user-id> | edit=true`; omitting the final
flag creates a read-only share.

`POST /api/commands/transcribe` accepts an authenticated multipart upload named `audio` (up to
25 MB). Nevma uses OpenAI for automatic Greek/English transcription and bilingual assistant
responses. Add these values to the ignored local `.env`, then restart the local stack:

```powershell
CommandAI__Enabled=true
CommandAI__ApiKey=<secret>
CommandAI__Endpoint=https://api.openai.com/v1/responses
CommandAI__Model=gpt-4o-mini
CommandAI__TranscriptionEndpoint=https://api.openai.com/v1/audio/transcriptions
CommandAI__TranscriptionModel=gpt-4o-transcribe
```

The assistant receives the user's local date/time, selected calendar date, and a bounded list of
their real calendar events. It can answer in Greek or English and propose creating or updating an
appointment. Every proposal is strictly deserialized and validated again inside Nevma; AI output
can never bypass authorization, preview, explicit user confirmation, or the ten-year scheduling
limit. API keys remain on the backend and are never sent to Expo clients.

## Gateway and observability

The Gateway applies strict configured CORS, per-IP rate limiting, request-size limits, correlation
IDs, security headers, and reverse-proxy routing. Replace `Cors__AllowedOrigins` in every deployed
environment; do not use wildcard origins with credentials.

All deployables collect ASP.NET Core, HTTP client, runtime, trace, metric, and structured log
telemetry. OTLP export is disabled by default and can be enabled without code changes:

```powershell
$env:OpenTelemetry__Otlp__Enabled="true"
$env:OpenTelemetry__Otlp__Endpoint="https://otel-collector.example.com:4317"
```

Run database migrations as a controlled deployment step before starting each service. Back up each
service database independently, persist Data Protection keys, rotate secrets, and alert on health,
outbox backlog, dead-letter queues, push failures, elevated 401/403/429 rates, and command failures.

Planning records integration events in its PostgreSQL transactional outbox. RabbitMQ delivery
is opt-in locally and requires the broker URI to come from environment configuration:

```powershell
$env:MessageBroker__Enabled="true"
$env:MessageBroker__Uri="amqps://<user>:<password>@<host>/<vhost>"
```

Publisher confirms are enabled, and failed deliveries remain in the outbox for retry. No broker
credentials are stored in source control. Messaging consumes these events through a durable
quorum queue, records processed event IDs in its inbox, and dead-letters malformed payloads.

## Containers and production deployment

All seven deployables use the root multi-stage `Dockerfile`. The final image runs as the built-in
non-root `app` user, disables diagnostics, and contains only the ASP.NET runtime plus the published
service. The production Compose overlay adds a one-shot EF migration job, ClamAV, API health checks,
read-only filesystems, dropped Linux capabilities, and persistent volumes for files and protected
Data Protection key rings.

```bash
cp .env.production.example .env.production
# Fill every required secret and public HTTPS origin/issuer.
docker compose \
  --env-file .env.production \
  -f compose.yaml \
  -f compose.backend.yaml \
  config
docker compose \
  --env-file .env.production \
  -f compose.yaml \
  -f compose.backend.yaml \
  up -d --build --wait
```

`IDENTITY_CERTIFICATES_PATH` must be a read-only host directory containing `signing.pfx`,
`encryption.pfx`, and `data-protection.pfx`, each with a private key and a separately managed
password. Never copy these files into an image or Git. Place an HTTPS ingress in front of the
published Gateway port and the Identity port; `NEVMA_PUBLIC_ISSUER` must exactly match the public
Identity URL used in issued tokens. The Compose file binds both ports to loopback so the ingress is
the only public entry point.

Run database migrations as a controlled release step, verify health before shifting traffic, and
back up PostgreSQL, RabbitMQ, file storage, and both Data Protection key volumes. S3, Firebase,
SMTP, AI, and speech integrations remain disabled until their external credentials are supplied.

The GitHub workflow performs a pinned-SDK Release build, all tests, the live OAuth/RabbitMQ/Redis
workflow, transitive NuGet vulnerability audit, all seven container builds, and fixable high/critical
container vulnerability scans. Dependabot monitors NuGet, GitHub Actions, and Docker dependencies.
