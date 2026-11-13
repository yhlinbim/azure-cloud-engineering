## Projects

| Service | Status | What it does |
|---------|--------|---------------|
| Azure Functions | Done | HTTP-triggered API for lightweight, event-driven queries |
| Service Bus | Done | Queue-triggered Function processing async messages; idempotency enforced by a UNIQUE constraint, malformed messages dead-lettered immediately |
| API Management | Done | Gateway in front of both HTTP Functions — subscription key auth, rate limiting; forwards the Function key so the Functions can't be called directly |
| Logic Apps | Partial | Configured Service Bus trigger + Compose action; trigger never fired in testing, root cause not found |
| Blob Storage | Done | HTTP-triggered upload endpoint, Managed Identity auth; 10 MB limit, PDF/JPEG/PNG only (verified by magic bytes) |

## Azure Functions — GetDailyReportStats

HTTP-triggered function (.NET 8 Isolated, Flex Consumption plan) that
queries CDRS's Azure SQL database and returns lightweight statistics —
today's new report count and pending review count.

**Key design decisions:**
- Uses Dapper instead of EF Core — this is a single read-only query with
  no domain modeling needed, and Dapper has near-zero cold-start overhead
  compared to spinning up a full DbContext, which matters for a Function
  that scales to zero.
- Connects to Azure SQL via Managed Identity (no password anywhere) —
  a dedicated read-only SQL user, scoped to SELECT on DailyReports only.
- Deployed via Flex Consumption's "One Deploy" mechanism using Azure
  Functions Core Tools — the traditional Zip Deploy used by Visual
  Studio's Publish wizard isn't supported on this hosting plan.
- Shares Application Insights with CDRS.Web (`appi-cdrs-poc`) rather than
  using its own resource, enabling correlated monitoring across services.

Endpoint (via API Management): `https://apim-cdrs-poc.azure-api.net/reports/GetDailyReportStats`
— requires an `Ocp-Apim-Subscription-Key` header. The Function itself uses
`AuthorizationLevel.Function` and APIM injects the Function key, so calling
the Function URL directly returns 401.

## Database

`NotificationLog` (written by the Service Bus consumer) is owned by this repo.
Scripts in `database/` are numbered and idempotent — run them in order against
a new environment; on an existing database, run only the ones not yet applied.

| Script | Purpose |
|--------|---------|
| `001_create_NotificationLog.sql` | Baseline — the table was originally created by hand |
| `002_add_unique_NotificationLog.sql` | UNIQUE (ReportId, EventType, TriggeredAtUtc) for idempotency |