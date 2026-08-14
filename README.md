## Projects

| Service | Status | What it does |
|---------|--------|---------------|
| Azure Functions | Done | HTTP-triggered API for lightweight, event-driven queries |
| Service Bus | Planned | Queue-triggered Function processing async messages |
| API Management | Planned | API gateway fronting multiple backend services |
| Logic Apps | Planned | Workflow automation without custom code |
| Blob Storage | Planned | File upload/processing pipeline |

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

Live endpoint: `https://func-cdrs-reportstats-babkcyh0a0h8fnek.australiaeast-01.azurewebsites.net/api/getdailyreportstats`