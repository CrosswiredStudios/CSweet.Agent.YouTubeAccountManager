# Grants

The installation requests only brokered platform capabilities. C-Sweet retains authorization and storage authority.

- `web.fetch.v1` and `web.request.v1`: exact YouTube origins and paths declared in the manifest; OAuth tokens are injected by C-Sweet.
- `platform.media.transfer.v1`: approved organization media assets only; the platform owns resumable upload sessions, checksums, limits, retries, and progress.
- `platform.managed-action.execute.v1`: binds approval or standing-policy authorization to the exact installation, channel, payload hash, revision, resource, and idempotency key.
- `platform.engagement-inbox.upsert.v1`: deduplicated comments and engagement state.
- `platform.metric-snapshot.write.v1`: official, non-revenue YouTube metrics.
- `platform.synchronization-checkpoint.v1`: restart-safe comment and report synchronization.
- `platform.business-profile.read.v1`: approved organization identity for private, approval-governed reply drafts.

Base Google permission sets are read-only. Publishing, management/live, memberships, and partner access are separate user-initiated progressive grants. The agent cannot request a scope that is not declared in `csweet-plugin.json`.
