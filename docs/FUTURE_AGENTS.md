# Instructions for future agents

Read this before changing architecture.

## Intent to preserve

- **Nomad is the preferred safe-ish execution backend.**
- **SSH exists, but only for explicit accept-the-risk usage.**
- Skills must stay **config-driven** rather than hardcoded.
- Sanitization must run by default on every interaction.
- Git should remain the default source-sharing mechanism.
- Blob is the first shared storage target; S3-compatible providers can follow behind the same abstraction.

## Rules to preserve

1. Do not let raw SSH become the default path.
2. Do not silently enable risky skills.
3. Keep elevation explicit and auditable.
4. Keep backend differences hidden behind the same session API.
5. Prefer skill execution over arbitrary command strings.
6. Keep worktree and artifact handling separate.

## Good next actions

- wire a real SSH client package
- wire a real Nomad client / HTTP integration
- add persistence
- add policy editing UI
- add runtime detection on hosts
- add health checks and leases for worktree ownership

## Things to challenge later

- whether Blob snapshots are enough or if you need a real shared filesystem
- whether approval should be synchronous or queued
- whether sub-agent shared resources should be copy-on-write instead of shared-write
