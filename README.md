# Charley Company Dashboard

Blazor Server dashboard for Charley Company operations metrics.

## Included

- ASP.NET Core Identity authentication.
- Venture group dashboard at `/` for Charlie Company Ventures Group.
- Local service entity dashboards at routes such as `/entity/nashville`, `/entity/knoxville`, and `/entity/chattanooga`.
- Venture finance dashboard at `/finance`.
- Local finance dashboards at routes such as `/entity/nashville/finance`, `/entity/knoxville/finance`, and `/entity/chattanooga/finance`.
- Temporary finance feature set modeled from `Charlie_Company_Financial_Reconstruction_Workbook.xlsx`, including:
  - Control assumptions.
  - Bank reconciliation.
  - Revenue collections.
  - Accounts payable aging.
  - Debt schedule.
  - Owner benefit analysis.
  - Job profitability.
  - Cash flow reconstruction.
  - 13-week cash forecast.
  - True financial position.
  - CCV readiness scorecard.
- Dashboard cards for:
  - Current jobs in progress.
  - Outstanding estimates.
  - Expired estimates.
  - Expenses this month.
  - Revenue this month.
- Housecall Pro API client shell with placeholder API key configuration.
- Mock dashboard data until a real Housecall Pro API key is configured.
- Housecall Pro webhook endpoint at `/api/housecallpro/webhooks`.
- Notification recipient entry/edit screen at `/notifications`.
- Webhook-triggered notification dispatch for estimate and expense events.
- SMTP email sender that logs simulated emails until SMTP is configured.
- SMS and iMessage provider hooks that log simulated delivery until providers are configured.
- Background sync service.
- Serilog console logging.
- Serilog rolling file logs in `Logs/charley-dashboard-.log` with hourly rolling filenames.

## Layered Estimate Costing

Costing policies are effective-dated, immutable revisions. A company-wide policy is the fallback; the active local-operation policy is a complete inherited override. Existing estimate cost snapshots retain the policy revision and supply prices used when they were created.

Estimate pricing combines accepted CentCom materials, automatic standard supply kits, task/work-type crew rate cards, additional task and project costs, general-overhead allocation, contingency, and task-specific target margins. Task-level overrides remain available for exceptional projects.

Material exclusions remain in the CentCom audit trail and must identify their recovery method and reference. An unmapped exclusion produces a warning rather than silently implying that its cost has been recovered.

## Run Locally

The application uses PostgreSQL. Store the development connection string in .NET User Secrets so the password is never committed:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=10.168.168.9;Port=5432;Database=CharlieCompany;Username=charlie_app;Password=YOUR_PASSWORD"
```

Alternatively, set `ConnectionStrings__DefaultConnection` as an environment variable. Entity Framework applies pending migrations automatically at startup.

## Roles And Local Operations

The application uses `Administrator` and `LocalOperator` Identity roles. Configure the first administrator through environment variables or User Secrets; never commit the bootstrap password:

```powershell
dotnet user-secrets set "BootstrapAdmin:Email" "admin@example.com"
dotnet user-secrets set "BootstrapAdmin:Password" "A-STRONG-UNIQUE-PASSWORD"
```

In production, use `BootstrapAdmin__Email` and `BootstrapAdmin__Password`. After the first administrator is created and assigned, remove the bootstrap password from the environment. Administrators manage operations, local operator accounts, many-to-many operation assignments, and integration secret references at `/admin/operations`.

Creating an active operation automatically enables its `/entity/{slug}` and `/entity/{slug}/finance` dashboards. Local operators see only operations assigned to their account; administrators see the complete venture rollup.

## Consolidated Operations Console

Administrators can monitor structured, correlated workflow events at `/admin/operations-console`. Remote services submit events to `POST /api/observability/events` using the `X-CCV-Observability-Key` header. Configure secrets outside source control:

```text
Observability__IngestionKey=<strong-random-secret>
Observability__RetentionDays=30
```

Remote services that use the included buffered forwarder also configure:

```text
Observability__ForwardingUrl=https://<ccv-host>/api/observability/events
```

## Scheduled report delivery

Administrators can define daily reports at `/admin/scheduled-reports`, select a time zone and run time, and choose email, text, or both for each active notification recipient. Report execution is stored in PostgreSQL and protected by a unique schedule/date key so restarts cannot send the same daily report twice.

Set the public HTTPS application address in the production environment before enabling text delivery:

```text
ScheduledReports__PublicBaseUrl=https://<charlie-company-host>/
```

Email reports contain the report body. Text reports contain a short notice and a secure application link. A text recipient must match a Charlie Company user with a verified phone number and current transactional consent. Text links use a recipient-specific cryptographic bearer token. Only a SHA-256 hash is stored, and the link expires after `ScheduledReports__AccessLinkHours` (seven days by default). The report can be read without signing in, and the page provides a login action that returns the user to the relevant Charlie Company workflow.

Every multi-service workflow should keep the same `correlationId` across Housecall Pro, CompanyCam, CentCom, pricing, approval, and delivery steps. The ingestion endpoint is rate limited, deduplicates `eventId`, redacts common secret patterns, and rejects unknown local-operation identifiers. CSV diagnostic export requires the Administrator role.

```powershell
dotnet run
```

Or use a specific HTTP port:

```powershell
dotnet run --urls http://localhost:5088
```

The dashboard route `/` is protected by ASP.NET Core Identity. Register or log in to view the Charlie Company Ventures Group rollup. Click any local service entity card to view that entity's own dashboard.

The notification recipient screen `/notifications` is also protected by ASP.NET Core Identity.

The finance screen `/finance` is protected by ASP.NET Core Identity. It shows a venture-group finance rollup and links to each local entity's independent finance dashboard.

## Configure Housecall Pro

No public Housecall Pro test API key was found during setup, so the app uses mock data by default.

For local development, prefer .NET user-secrets:

```powershell
dotnet user-secrets set "HousecallPro:ApiKey" "YOUR_REAL_API_KEY"
dotnet user-secrets set "HousecallPro:UseMockDataWhenApiKeyIsPlaceholder" "false"
```

For production, use Azure Key Vault or environment variables. Do not commit a real Housecall Pro API key.

## Test The Webhook Endpoint

```powershell
Invoke-WebRequest `
  -Uri "http://localhost:5088/api/housecallpro/webhooks" `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"event":"estimate.completed","id":"demo-estimate"}'
```

## Configure Notifications

Email can be sent through SMTP by setting the `Notifications:Email` configuration values. During development, keep `SmtpHost` blank to log simulated email notifications without sending real messages.

```powershell
dotnet user-secrets set "Notifications:Email:SmtpHost" "smtp.example.com"
dotnet user-secrets set "Notifications:Email:FromAddress" "dashboard@charleycompany.com"
dotnet user-secrets set "Notifications:Email:UserName" "smtp-user"
dotnet user-secrets set "Notifications:Email:Password" "smtp-password"
```

### Resend email

Resend can send Identity messages (forgot-password and administrator password-reset links) and operator event notifications. Verify a sending domain in Resend, create a sending-only API key, and keep the key outside source control.

```powershell
dotnet user-secrets set "Email:Provider" "Resend"
dotnet user-secrets set "Email:ResendApiKey" "re_replace_with_real_key"
dotnet user-secrets set "Email:FromAddress" "notifications@your-verified-domain.example"
dotnet user-secrets set "Email:FromName" "Charlie Company Ventures"
dotnet user-secrets set "Email:ReplyToAddress" "operations@your-domain.example"
```

Production uses the equivalent environment variables:

```text
Email__Provider=Resend
Email__ResendApiKey=re_replace_with_real_key
Email__FromAddress=notifications@your-verified-domain.example
Email__FromName=Charlie Company Ventures
Email__ReplyToAddress=operations@your-domain.example
```

SMS and iMessage notifications are represented as provider hooks. SMS should be wired to a provider such as Twilio or Azure Communication Services. Server-side iMessage delivery requires an approved Apple messaging integration, such as Apple Messages for Business; the app logs simulated iMessage notifications until that provider is configured.
