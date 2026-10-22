# Production readiness backlog

Status: Paused by product decision on 2026-10-22. Resume before the first production release.

## Required before release

- Add fail-fast validation for every required production environment variable and secret.
- Define and test the production HTTPS, trusted proxy, CORS, callback URL, and service URL configuration.
- Package and verify the speech-to-text model/service in the production deployment.
- Add an end-to-end test covering microphone upload, transcription, bilingual command parsing, confirmation, and calendar persistence.
- Replace or front the public Photon/OpenStreetMap endpoint with a production-supported location and POI search service, including rate limits, caching, timeouts, and fallback behavior.
- Automate database migrations with a tested rollback procedure.
- Define encrypted backups and run a restore drill.
- Verify monitoring, alerting, structured logs, and health/readiness probes in the deployed environment.
- Run security, load, failure-recovery, Web, Android, and iOS acceptance tests.
- Complete a staging deployment and release checklist before declaring the product production-ready.

## Already present

- HTTPS redirection support.
- Gateway and authentication rate limiting.
- Service health endpoints.
- Monitoring configuration.
- Production environment template.
- Docker packaging for the local speech-to-text service.

