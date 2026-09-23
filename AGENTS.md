# CharlieCompany project guidance

- Build with `dotnet build CharlieCompany.slnx --disable-build-servers -p:UseSharedCompilation=false`.
- Keep reporting, dashboard, estimate, finance, account, and reconciliation rules aligned. Preserve existing business behavior unless explicitly changed.
- Treat PostgreSQL migrations and DB01 access as approval-required. Do not run migrations against a shared database without confirming the target and obtaining approval.
- Treat API01/Web deployment, service restarts, protected settings, and public health checks as separate approval-required stages.
- Never expose or commit connection strings, finance data, customer information, or protected environment values.
- Standing delivery instruction: for requested changes, build, commit all changes, push to the remote repository, and publish live until the user revokes this instruction. Do not infer authorization to add or run tests unless requested.
