---
created: 2026-03-10T13:02:45.642Z
title: Add Aspire AppHost for Service and Web projects
area: general
files:
  - src/AgentHub.Service/Program.cs
  - src/AgentHub.Web/Program.cs
  - src/AgentHub.Service/AgentHub.Service.csproj
  - src/AgentHub.Web/AgentHub.Web.csproj
---

## Problem

The Service and Web projects are currently launched and configured independently. There is no unified orchestration for local development — connection strings, ports, and dependencies are manually wired. Integration testing doesn't leverage shared infrastructure orchestration.

## Solution

Wrap both projects with a .NET Aspire AppHost to run and monitor them together:

1. **Create AppHost project** (`src/AgentHub.AppHost/`) that references Service and Web, defining the full topology in code
2. **Create ServiceDefaults project** (`src/AgentHub.ServiceDefaults/`) for shared telemetry, health checks, and resilience configuration
3. **Replace manual plumbing** with Aspire service discovery for inter-service communication
4. **Use official Aspire integrations** where available (SQLite via `Aspire.Hosting.SQLite`/`Aspire.SQLite`)
5. **Wrap containers as custom extensions** (in their own projects) when no official integration exists — prefer full Aspire extensions over raw container definitions
6. **Replace/augment integration tests** with `Aspire.Hosting.Testing` and `DistributedApplicationTestingBuilder`
7. **Leverage Aspire Dashboard** for unified logs, traces, and metrics across both services

### Key references
- Aspire overview: https://aspire.dev/get-started/what-is-aspire/
- AppHost setup: https://aspire.dev/get-started/app-host/
- Integrations gallery: https://aspire.dev/integrations/gallery/
- Testing: https://aspire.dev/testing/overview/
- Research notes: memory/aspire-research.md

### Preferences
- Prefer full Aspire extensions over raw containers
- When no official extension exists, wrap containers as custom extensions in their own projects
