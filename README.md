# Charley Company Dashboard

## CCVG connection and isolated webhook evaluation

Both CCVG capabilities default to disabled. Existing Nashville credential fallback,
local-operation credentials, and `/api/housecallpro/webhooks` are retained.
No database migration is required: mappings reuse `LocalOperation.HousecallProLocationId`.

Administrator tools are at `/admin/ccvg-connection`, also linked from Operations Admin.
Discovery explicitly reads HCP `/company`; it does not create operations. Administrators
can map discovered companies to existing operations and request one jobs/estimates
sample page (page size 1). Samples expose totals, not customer records, and do not import.
Mapping changes are logged with actor, operation, old/new company IDs and UTC time.
The existing integration secret-reference editor remains metadata only; it is not a
general-purpose secret-manager resolver.

Protected configuration names (configure through the deployment's approved secret mechanism):

| Setting | Purpose/default |
|---|---|
| `HousecallPro__Ccvg__Enabled` | Enables CCVG API use/discovery; false by default |
| `HousecallPro__Ccvg__ApiKey` | CCVG API credential; never enter in source or the administration page |
| `HousecallPro__Ccvg__OperationSlugs__0` (and subsequent indices) | Explicit operations that use CCVG for ordinary reads/imports; empty by default |
| `CcvgWebhookTest__Enabled` | Enables the isolated receiver; false by default |
| `CcvgWebhookTest__SigningSecret` | Dedicated CCVG HCP signing secret |
| `CcvgWebhookTest__AllowRegistrationChallenge` | Temporary acceptance of the exact unsigned `{"foo":"bar"}` registration request; false by default |
| `CcvgWebhookTest__Directory` | Separate private metadata directory, outside source and the web root |
| `CcvgWebhookTest__RetentionHours` | 24 by default, bounded to 1–168; pruning every 15 minutes while enabled |
| `CcvgWebhookTest__MaxEvents` | 1000 by default, bounded to 1–1000 |
| `CcvgWebhookTest__MaxBodyBytes` | 262144 by default, bounded to 1024–1048576 |
| `CcvgWebhookTest__TimestampToleranceSeconds` | 300 by default, bounded to 30–900 |

CCVG-selected operations require an active existing operation and a company mapping.
They use the umbrella credential plus `X-Company-Id`; a disabled/missing CCVG credential
does not fall back to Nashville. CCVG responses must carry matching record company IDs;
unrecognized or mismatched scoped responses fail closed. Invoice REST responses omit
company IDs: CCVG invoice lookup uses HCP's documented `X-Company-Id` scope on the
official invoice-list endpoint and checks any company identity supplied in a record.
It does not require invoice parent jobs to remain readable (historical invoices can
reference jobs returning 404). Payment-history lookup verifies the parent job first;
the job-specific URL provides context when history entries omit job/company IDs.
Any supplied job or company identity must agree with that context.
The CCVG transport uses only
the official HTTPS API host and does not follow redirects. Leave Nashville out of
`OperationSlugs` to keep its current connection. Establish mappings and use the sample
action before selecting operations for live synchronization. Retain the same company
identity when switching an existing operation; caches/imported records belong to that
operation and are not purged automatically by mapping edits.

The new anonymous ingress is **POST `/webhooks/housecallpro/ccvg`**. It returns 404 while
disabled. It never invokes the legacy webhook handler, imports, or notification services.
It authenticates `Api-Timestamp` and `Api-Signature` using HMAC-SHA256 over the timestamp,
a period, and the original body bytes; hexadecimal and Base64 encodings of the digest
are accepted with constant-time comparison. Timestamp tolerance, a 10-second read
deadline, bounded bodies, and a separate global 120-requests/minute limiter apply.
Missing signing configuration returns 503; invalid signatures return 401. Duplicates
are acknowledged without adding another receipt, using a payload hash within the
bounded retention window. The registration exception never becomes a trusted receipt;
disable it immediately after saving the HCP URL and configuring the signing secret.

Only verified event metadata is stored: receipt time, event type, company hash, mapped
operation ID (or unassigned), byte count, known root field names, and payload hash.
Raw bodies, signatures, API keys, customer values and financial values are not stored.
Metadata appears on the Administrator page with event/time filters and pagination;
the Administrator-only GET `/api/admin/ccvg/webhook-receipts` also supports `page`,
`eventType`, `operationId`, and `fromUtc`. Metadata reads are audit logged.

Provision the metadata directory with access restricted to the service identity and
administrators. Use one writer process per directory, with separate directories for
separate application instances. Pruning pauses while the feature/service is stopped;
account for that in operational retention procedures. Acknowledgement means receipt
metadata was persisted, not that records were imported or any downstream action ran.

This source change does not create DNS, tunnels, certificates, public URLs or HCP
subscriptions. Deployment, protected settings and external registration are separate
steps. In particular `ccvgapi.healthcareautomation.services` is not made live by adding
this endpoint. No production activation or verification is implied.

Official protocol references:
[multi-location APIs](https://docs.housecallpro.com/docs/housecall-public-api/4c64fe617d191-multi-location-enabled-ap-is)
and [webhook signing](https://docs.housecallpro.com/docs/housecall-public-api/46e9e1be07621-webhooks).

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

### Persistent close-rate cache

The close-rate panel reads an encrypted, per-operation cache independently of the operational record sync. The dedicated close-rate worker performs one initial import, then reads records in descending `updated_at` order until it reaches the last successful checkpoint (with a ten-minute overlap). Changes to historical jobs replace their previous contributions. A failed refresh retains the previous complete snapshot and checkpoint. The panel never waits for a network refresh when saved totals exist.

`HousecallPro:CloseRateCacheDirectory` controls the cache location. On Linux it defaults to `/var/lib/charlie-company/close-rate-cache`. Deployment must apply the repository systemd unit's `StateDirectory`, `StateDirectoryMode`, and additional `ReadWritePaths` directives and reload systemd before restarting. The directory and existing persistent Data Protection key ring must survive release replacement; cache files are encrypted with that key ring. No shared-database migration is required. The first deployment of this cache still requires its initial import. Locations without configured Housecall Pro integrations remain unavailable.

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
