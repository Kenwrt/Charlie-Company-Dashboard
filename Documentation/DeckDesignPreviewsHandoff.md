# Design Previews handoff

Design Previews appears within each saved Deck or Covered Deck / Roof Structure option when enabled. Use Suggested pictures to capture a full overview and the supporting views. The selected photo must be an original JPEG, PNG, or WebP within 8 MiB. Select a primary photo and enter the customer approval reference before authorizing the transfer to Google. Supporting views can be used for comparison or submitted as separate previews.

The server composes the design from the selected Deck patterns and material controls, the saved option description, and the task scope and measurements. Record colors, board direction, border details, railing, stairs, landings, and requested plant or furniture removal in the saved option or task scope. The gateway accepts up to 4,000 characters of design text. Estimate measurements, quantities, costs, and pricing remain authoritative.

## Configuration and private connectivity

The feature defaults to disabled. After an approved private route is available, set these through the dashboard's protected configuration on API01.

| Setting | Purpose |
| --- | --- |
| `DeckPreviews__Enabled` | Explicit feature switch. |
| `DeckPreviews__GatewayUrl` | HTTPS origin for the private gateway route, with a certificate API01 trusts. |
| `DeckPreviews__GatewayApiKey` | Bearer credential for the gateway's authenticated dashboard connection. |
| `DeckPreviews__StoragePath` | Dedicated absolute path outside the application and web roots, writable only by the dashboard service and accessible to authorized backup operators. Use a persistent API01 volume. |

The gateway currently listens on WrightServer loopback port 5198. API01 requires a separately approved, private authenticated route that forwards the contract paths to that listener. Keep the switch off pending that route. The dashboard uses normal HTTPS certificate validation and disables redirects. The Gemini credential stays in the gateway. Configure and preserve the gateway's stable storage and caller identity so a repeat request reaches the same idempotency ledger.

## Persistence and review

Private files are grouped by estimate ID, estimate version ID, task ID, and option ID. Each encrypted metadata record links the selected attachment ID and saved estimate snapshot hash to the acting user, consent timestamp, approval reference, exact gateway request body, and idempotency key. The key is the dashboard preview record ID in compact GUID format. The dashboard writes the record before sending the generation request. The output PNG is encrypted in the same private directory after retrieval. Back up this directory with the existing dashboard Data Protection key ring. Keep their permissions and application identity stable across releases. The pilot requires manual retention and removal of dashboard preview files and the corresponding gateway assets under the applicable customer retention policy.

Refresh status polls the gateway and retrieves a successful image. The original and result endpoints enforce the estimate's location access and check the version, task, option, record, and attachment relationship. Design changes mark same-version previews outdated. A new estimate version starts its own history. The estimator compares the original and result, confirms stairs, railings, footprint, and board layout, then downloads the reviewed image for manual proposal inclusion. Changes to the design disable that download. Stairs can drift even in an otherwise convincing image.

An uncertain submission can already have incurred a provider charge. The operator can explicitly reconcile a current pending submission using the same body and key. If the design became outdated or the gateway reports a conflict, resolve the outstanding record with the gateway operator before starting another preview for the option. A gateway NeedsReview state indicates provider uncertainty and receives no automatic resubmission. Cancellation is best effort and cannot guarantee that Google stops or reverses a charge. Queue saturation and the pilot reservation limit are reported separately. The default gateway cap is five total reservations, including unsuccessful or cancelled work.

Gateway contract source is available at [AI Media Gateway commit d1b58d8](https://github.com/Kenwrt/AI-Media-Gateway/tree/d1b58d8).
