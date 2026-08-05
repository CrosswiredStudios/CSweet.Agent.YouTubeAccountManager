# C-Sweet YouTube Account Manager

Protocol-v2, .NET 10 agent for managing one YouTube channel per C-Sweet installation. It supports brokered video publishing, metadata and playlist work, captions, comments and moderation, live configuration, eligible memberships/partner workflows, and official non-revenue analytics.

## Non-technical onboarding

Marketplace installation creates a `NeedsSetup` installation and opens C-Sweet's native setup wizard. Before activation, the runtime is restricted to the manifest's two bootstrap callbacks and connection-bound HTTP; ordinary work, events, chat, LLM, memory, and business capabilities are denied. The user reviews access, connects Google, grants the read-only base permissions, chooses a discovered channel, selects approval/privacy/cadence defaults, validates access, and activates. No Google Cloud Console, credential copying, token display, or plugin-hosted page is involved.

Optional features use progressive permission sets. Enabling publishing, content/live management, memberships, or partner workflows shows the exact additional access and starts a new user-initiated Google consent flow.

The settings surface also includes a platform-owned resumable media uploader. Users choose a video, thumbnail, WebVTT, or SubRip file; C-Sweet applies deployment and organization quotas, validates its signature and SHA-256 digest, cleans up abandoned sessions, and returns an opaque media-asset ID. Channel validation reports what Google can establish with the current permission set and labels features that require a progressive permission before eligibility can be checked.

## Security model

The agent receives only an opaque connection name and organization media-asset IDs. C-Sweet owns OAuth with authorization code plus PKCE, state/replay protection, provider profiles, encrypted token storage, refresh/revocation, exact origin/path/method checks, media streaming, secret extraction, approvals, audit, and disconnect purge. Live stream keys and upload session URLs never enter the agent runtime.

Every public mutation is sent to the platform's managed-action boundary. Manager Approval and CEO Approval route mutations to the selected approver. Fully Autonomous mode remains approval-routed unless the platform has an explicit owner-approved standing policy; no policy can be created by this agent. Destructive, live transition, rights, ownership, and monetization operations always require explicit approval.

Comments are synchronized every 15 minutes into the platform engagement inbox. Potentially urgent new comments generate a deduplicated alert in the protected approver conversation, and the platform emits at most one engagement digest per UTC day. Draft state is kept with each comment so brand-grounded reply preparation and all public replies remain approval-governed.

## Development

The published project pins `CSweet.Agent.SDK` 3.1.0. To test against a local SDK checkout:

```powershell
dotnet test CSweet.Agent.YouTubeAccountManager.slnx -p:UseLocalCSweetAgentSdk=true -p:CSweetAgentSdkRepositoryRoot=C:\path\to\CSweetAgentSdk
```

Tests use `AgentTestRuntime` and deterministic fake platform/YouTube responses; real credentials are never required.
