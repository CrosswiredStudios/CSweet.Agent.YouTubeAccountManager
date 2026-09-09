# Repository guidance

- This repository contains one protocol-v2 C-Sweet agent. Keep `csweet-plugin.json`, `AgentId`, and `Version` synchronized.
- Target .NET 10 and `CSweet.Agent.SDK` 3.40.0. Keep one concurrent job until every provider mutation is proven idempotent.
- The agent must never accept, store, log, or return OAuth client secrets, access tokens, refresh tokens, upload session URLs, or live stream keys.
- All YouTube traffic must use a manifest-declared credential-bound platform operation and the confirmed channel binding.
- Public mutations must pass through `platform.managed-action.execute.v1`; permanent deletion, bans, go-live, rights, ownership, and monetization changes are always hard-gated.
- Media inputs are organization media-asset IDs. Do not add filesystem paths, arbitrary URLs, transcoding, or local credential fallbacks.
- Setup and settings are declarative, platform-rendered flows. Do not add web pages, scripts, Razor, iframes, redirects, or remote UI.
- Tests use `AgentTestRuntime` and deterministic fakes only. Never require real Google credentials or a running C-Sweet instance.
- Before release run `dotnet test CSweet.Agent.YouTubeAccountManager.slnx` and validate the root manifest with SDK 3.40.0.

## Release-note ordering

- Bump the agent version FIRST, synchronizing the root `csweet-plugin.json`, implementation identity, project/package version, and version assertions as required by this repository.
- Only AFTER the version bump, read the final `version` back from `csweet-plugin.json` and write `releases/<version>.md` for that exact version (no `v` prefix). Never write the new release's notes under the previous version.
- If the version changes again during the task, retarget the unpublished notes to the final version. Preserve already published historical notes.
- Before handoff or publishing, verify that the manifest, implementation/package version, release-note filename, and release-note heading all match. A version bump is incomplete without its matching release notes.
