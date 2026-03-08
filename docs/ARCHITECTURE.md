# Architecture

## Goal

Manage agent sessions across multiple machines with a consistent control plane, while keeping direct-host execution available only as an explicit accept-the-risk path.

## Recommended v1 shape

- **Coordinator API**
  - owns user-facing sessions API
  - stores session metadata
  - routes to execution backends
  - streams events via SSE
- **Backends**
  - `nomad`: preferred scheduler-backed execution path
  - `ssh`: local/direct host execution path with explicit risk acceptance
- **Shared code / storage**
  - Git for source checkout/worktree identity
  - Blob for artifacts, snapshots, and cross-device persistence

## Core flow

1. Client requests session start.
2. Coordinator loads host inventory from registered backends.
3. Placement engine selects backend + host from requirements.
4. Backend starts the session and emits events.
5. Client subscribes via SSE and sends skill-based inputs.
6. Sanitizer + policy layer decide whether the input is allowed.
7. Backend executes or rejects, then emits audit / output events.

## Why not SSH-only

SSH is useful and fast, but it is not the default path because:

- weak sandboxing compared to scheduler/container-backed execution
- more host blast radius
- harder resource accounting
- trickier multi-tenant safety

That is why SSH is modeled as:

- explicit backend
- explicit host allowlist
- explicit `AcceptRisk = true`
- skill-gated by policy

## Why Git + Blob

Git handles:

- source sharing
- commit pinning
- branch-based collaboration
- sparse/shallow strategies

Blob handles:

- artifacts
- snapshots
- large outputs
- future cross-host synchronization

## Future target

A mature version should have:

- real auth and user/role policy
- durable data store
- durable event bus
- real Nomad adapter
- real SSH transport with key management
- approval flows for elevated skills
- host health / inventory refresh
- package/runtime capability detection on hosts
