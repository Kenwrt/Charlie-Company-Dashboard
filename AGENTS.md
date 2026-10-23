# Charlie Company Active Dashboard working agreements

## Workspace and preservation
- Active workspace: `C:\Dev\CharlieCompanyActiveDashboard`.
- This is a separate checkout of `https://github.com/Kenwrt/Charlie-Company-Dashboard.git`.
- Preserve the old saved project Charlie Company Active New at `C:\Dev\CharlieCompany`, including all modified, untracked, ignored, and protected files. Do not edit, move, clean, stash, reset, or delete that checkout.
- Inspect the current branch and working tree before editing. Preserve unrelated work and stage only task-scoped files when a commit is requested.
- Keep execution, dependencies, development services, and source on WrightServer or its VMs.

## Application and commands
- This is the existing .NET 10 Blazor internal business dashboard, with reporting, estimates, finance, accounts, catalog automation, and reconciliation.
- Web project: `CharleyCompany.Dashboard.Web.csproj` (retain the existing spelling).
- Solution: `CharlieCompany.slnx`.
- Local NuGet source: `packages`, resolved relative to this checkout by `NuGet.config`.
- Build only when explicitly requested: `dotnet build CharlieCompany.slnx --disable-build-servers -p:UseSharedCompilation=false`.
- Do not add or run tests, linters, browser checks, health checks, or other validation unless specifically requested in the current task.
- Preserve existing business behavior unless explicitly changed.

## Tools and authorization
- `.codex/config.toml` follows TargetLines' network proxy configuration.
- `.codex/rules/wrightserver.rules` follows TargetLines' command-policy structure, adapted to this project. Command allowances do not authorize actions outside the user's request.
- An explicit commit, push, merge, publish, release, or complete-delivery request authorizes its routine Git steps. Do not force-push or overwrite concurrent work.
- No automatic build, commit, push, deployment, or live publication applies merely because a source change was requested.
- This setup explicitly prohibits deployment. Do not publish, restart services, modify shared databases, or change remote configuration during setup.
- Stop and report a requested operation's error without launching unsolicited diagnostics.

## Service topology and future deployment boundary
- API01 hosts `charlie-company.service`; application root is `/opt/wrightapps/charlie-company`, with the active release at `/opt/wrightapps/charlie-company/current`.
- Existing publish profile: `Properties/PublishProfiles/API01.pubxml`, targeting .NET 10 linux-x64, framework-dependent Release output under `bin/Release/net10.0/linux-x64/publish/API01/`.
- Only for an explicitly requested future publication: `dotnet publish CharleyCompany.Dashboard.Web.csproj -p:PublishProfile=API01 --disable-build-servers -p:UseSharedCompilation=false`.
- For an explicitly requested future deployment, preserve the existing release procedure and rollback release. The documented restart command is `sudo -n /usr/bin/systemctl restart charlie-company.service`, executed as ken on API01.
- WEB01 is an SSH target for requested connectivity checks. Do not copy TargetLines' WEB01 container, private port 5103, domain, or service identifiers into this application.
- DB01/PostgreSQL access, shared-database migrations, credential changes, infrastructure changes, and live messaging or billing require explicit task scope.
- Never expose administrative or development services directly to the public Internet.

## Protected data and reporting
- Never print, copy, retain, or commit secret values, protected environment files, private keys, credentials, customer data, finance data, or data-protection keys.
- Do not copy ignored runtime state or protected settings from the old checkout. Resolve required protected settings through an authorized protected mechanism when needed.
- Report build, test, artifact, signing, commit, push, deployment, health checks, provider state, and external-store status separately. Do not imply one stage proves another.
