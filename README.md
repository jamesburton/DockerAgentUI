# AgentSafeEnv

Windows-friendly starter for managing agent sessions across multiple machines with:

- **Nomad-first orchestration** for scheduled/sandboxed sessions
- **SSH adapter** for explicit accept-the-risk local/direct host execution
- **ASP.NET Core / .NET 10 SSE** event streaming
- **Aspire AppHost** skeleton
- **MAUI client** skeleton
- **config-driven skills, policies, and sanitization**
- **Git worktrees + Blob-backed shared artifacts/snapshots**

## Current status

This archive is a **kickoff scaffold**, not a finished production system.

What is in place:

- central service with session CRUD, host listing, policy listing, skill listing, and SSE stream endpoints
- orchestration abstractions for backends, routing, sanitization, skill policy resolution, shared storage, and worktree providers
- in-memory demo backend plus starter **Nomad** and **SSH** adapter shapes
- sample config for skills, policies, sanitizers, hosts, and storage
- docs for architecture, roadmap, and future-agent continuation instructions

What is not yet complete:

- no tested end-to-end build in this environment
- Nomad and SSH adapters are stubs / partial implementations
- no real auth
- no database
- no Redis / NATS event bus
- no full MAUI admin UX yet

## Suggested next implementation order

1. Build and run the API service locally.
2. Wire config loading and endpoints.
3. Finish one real backend first:
   - **SSH** if you want the fastest direct-control prototype
   - **Nomad** if you want cleaner scheduler-first execution
4. Replace the in-memory event bus and stores.
5. Add approval / elevation flows and host policy enforcement.

## Repo layout

- `src/AgentHub.Contracts` shared DTOs
- `src/AgentHub.Orchestration` orchestration core
- `src/AgentHub.Service` central API
- `src/AgentHub.AppHost` Aspire host
- `src/AgentHub.Maui` MAUI client starter
- `config/` skill manifests, policies, sanitizers, hosts, and storage examples
- `docs/` architecture, progress, and future-agent notes

See `docs/PROGRESS.md` and `docs/FUTURE_AGENTS.md` first.
