# Technology Stack

**Project:** AgentSafeEnv - Multi-Agent Orchestration Platform
**Researched:** 2026-03-08

## Recommended Stack

### Core Runtime & Framework

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| .NET 10 | 10.0.x (LTS) | Runtime | LTS until Nov 2028. Native SSE support, AOT improvements. Non-negotiable per constraints. | HIGH |
| ASP.NET Core 10 | 10.0.x | Web API / SSE | Native `Results.ServerSentEvents` for real-time streaming. No third-party SSE libraries needed. | HIGH |
| Aspire | 13.1.x | Orchestration / DevEx | Service discovery, OpenTelemetry dashboard, integration testing, health checks. Polyglot support if needed. | HIGH |
| C# 14 | — | Language | Ships with .NET 10. `field` keyword, extension members, null-conditional assignment. | HIGH |

### Database & Data Access

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| EF Core 10 | 10.0.x | ORM / Data Layer | Ships with .NET 10. Provider abstraction supports SQLite, SQL Server, Postgres. AUTOINCREMENT control improvements. | HIGH |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.3 | Initial DB Provider | Zero-config, file-based, ideal for single-operator MVP. Swap to Postgres/SQL Server later via EF provider abstraction. | HIGH |
| Microsoft.EntityFrameworkCore.Design | 10.0.x | Migrations tooling | Required for `dotnet ef` migrations CLI. Dev dependency only. | HIGH |

### CLI Tooling

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| System.CommandLine | 2.0.0 | CLI framework | Finally stable (shipped Nov 2025 with .NET 10). First-party Microsoft. NativeAOT support, 40% faster parsing vs beta4. | HIGH |
| Spectre.Console | 0.54.0 | Rich terminal output | Tables, progress bars, live rendering for agent status display. Mature, widely adopted. | HIGH |
| Spectre.Console.Cli | 0.53.1 | CLI command routing | Alternative to System.CommandLine if richer UX needed. Can complement System.CommandLine for output only. Use Spectre.Console for rendering, System.CommandLine for parsing. | MEDIUM |

**Recommendation:** Use System.CommandLine for argument parsing (it is the Microsoft-blessed stable option) and Spectre.Console for rich output rendering (tables, live status, progress). They compose well together.

### Process Management & Remote Execution

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| CliWrap | 3.10.0 | Local process execution | Fluent API over System.Diagnostics.Process. Async streams for stdout/stderr capture. Essential for wrapping agent CLIs (claude, codex, gh copilot). | HIGH |
| SSH.NET | 2025.1.0 | Remote command execution | Mature SSH-2 library. Async support, key-based auth, AOT-compatible (.NET 8+). For SSH execution backend. | HIGH |

**Nomad integration:** No official .NET SDK exists. Use Nomad's REST API (`/v1/`) directly via HttpClient. Wrap in a thin `NomadClient` service. This is the right approach since Nomad's API is simple and stable.

### Real-Time Communication

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| ASP.NET Core SSE (built-in) | 10.0.x | Server-to-client streaming | .NET 10 has native `SseItem<T>` + `Results.ServerSentEvents()` via `IAsyncEnumerable<T>`. No library needed. Supports reconnection with Last-Event-ID. | HIGH |
| SignalR | 10.0.x | Future bi-directional (optional) | Only if approval/elevation flows need bi-directional real-time. SSE is sufficient for monitoring. Defer unless needed. | MEDIUM |

**Recommendation:** Start with native SSE for all agent event streaming and status updates. SSE is simpler, requires no client library, and handles the one-way server push pattern perfectly. Reserve SignalR only if interactive bi-directional flows prove necessary.

### AI / Agent Integration

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| ModelContextProtocol | 1.1.0 | MCP server/client SDK | Official Microsoft/Anthropic C# SDK. GA v1.0 shipped Feb 2026. For MCP-based agent control protocol bridge. | HIGH |
| ModelContextProtocol.AspNetCore | 1.1.0 | HTTP-based MCP server | For exposing MCP tools/resources over HTTP transport to agents. | HIGH |
| Microsoft.Extensions.AI.Abstractions | 10.3.0 | AI service abstraction | Unified IChatClient/IEmbeddingGenerator interfaces. Provider-agnostic. Useful if the platform itself needs to call LLMs (e.g., for summarization, routing). | MEDIUM |
| Microsoft.SemanticKernel | 1.x (GA) | Agent orchestration framework | Microsoft Agent Framework converging SK + AutoGen. Multi-agent orchestration patterns built-in. Consider ONLY if platform needs to orchestrate AI-to-AI workflows internally. | LOW |

**Recommendation:** Use ModelContextProtocol SDK for MCP protocol support. Use Microsoft.Extensions.AI if the coordinator needs to call LLMs directly. Do NOT use Semantic Kernel unless you need AI-to-AI multi-agent orchestration within the platform itself -- this platform orchestrates CLI-based agents, not LLM agents internally.

### Web UI

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Blazor (Interactive Server) | 10.0.x | Web dashboard | Ships with ASP.NET Core. Server-rendered for real-time updates. PersistentState attribute in .NET 10 for circuit reconnection. Ideal for dashboards. | HIGH |
| Blazor WebAssembly | 10.0.x | Optional client-side | Preloading improvements in .NET 10. Use for static/offline-capable views. Not needed initially. | LOW |

### Desktop/Mobile UI (Future)

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| .NET MAUI 10 | 10.0.x | Desktop/mobile client | Blazor Hybrid via BlazorWebView -- reuse Blazor components from web dashboard. HybridWebView.InvokeJavascriptAsync for native interop. | MEDIUM |

**Recommendation:** Build Blazor components for the web dashboard first. MAUI Blazor Hybrid can host the same Razor components later with minimal rework. This is the explicit Microsoft-recommended path.

### Observability & Telemetry

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| OpenTelemetry .NET SDK | 1.x | Traces, metrics, logs | Industry standard. Aspire configures it automatically via `ConfigureOpenTelemetry()`. | HIGH |
| Aspire Dashboard | 13.x | Dev-time observability | Built-in OTLP receiver. Visualizes traces, metrics, logs. Zero config with Aspire. Also available as standalone Docker container. | HIGH |

### Testing

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| xUnit | 2.9.x | Unit/integration tests | .NET standard. Aspire testing integration uses xUnit. | HIGH |
| Aspire.Hosting.Testing | 13.x | Distributed app testing | `DistributedApplicationTestingBuilder` for full integration tests with real services. Dynamic endpoint discovery. | HIGH |
| NSubstitute | 5.x | Mocking | Clean syntax, widely adopted in .NET ecosystem. | HIGH |

### Supporting Libraries

| Library | Version | Purpose | When to Use | Confidence |
|---------|---------|---------|-------------|------------|
| Polly | 8.x | Resilience & retries | HTTP calls to Nomad, SSH connections, agent CLI timeouts. Built into Microsoft.Extensions.Http.Resilience. | HIGH |
| FluentValidation | 11.x | Input validation | API request validation, config validation for skills/policies. | HIGH |
| System.Text.Json | 10.0.x | JSON serialization | Ships with .NET 10. Use over Newtonsoft.Json -- faster, AOT-compatible. | HIGH |
| Serilog | 4.x | Structured logging | Rich sinks ecosystem. Integrates with OpenTelemetry. Alternative: just use Microsoft.Extensions.Logging with OTel (simpler). | MEDIUM |
| MediatR | 12.x | In-process messaging | CQRS pattern for command/query separation in coordinator API. Optional but helpful for clean architecture. | MEDIUM |

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| CLI Parsing | System.CommandLine 2.0 | Spectre.Console.Cli | System.CommandLine is now stable + first-party. Use Spectre for output only. |
| SSE/Real-time | ASP.NET Core native SSE | SignalR | SSE is simpler for one-way streaming. SignalR adds unnecessary complexity for monitoring. |
| ORM | EF Core 10 | Dapper | EF Core handles migrations, change tracking. Dapper is micro-ORM; adds friction for schema evolution. |
| JSON | System.Text.Json | Newtonsoft.Json | STJ is faster, AOT-compatible, ships with runtime. Newtonsoft is legacy. |
| Process Mgmt | CliWrap | System.Diagnostics.Process | CliWrap handles async streams, piping, cancellation with clean API. Raw Process is tedious and error-prone. |
| Logging | Microsoft.Extensions.Logging + OTel | Serilog | Serilog adds a dependency for marginal benefit when OTel handles structured logging. Start simple. |
| Agent Orchestration | ModelContextProtocol SDK | Semantic Kernel | SK is for AI-to-AI orchestration. This platform wraps CLI tools, not LLM agents. |
| Web UI | Blazor Interactive Server | React/Angular | Blazor keeps entire stack in C#/.NET. No JS build pipeline. Reusable in MAUI Hybrid. |
| HTTP Client (Nomad) | Raw HttpClient + typed wrapper | Third-party Nomad SDK | No official .NET SDK exists. Nomad REST API is simple. Thin wrapper is better than unmaintained community libs. |

## What NOT to Use

| Technology | Why Not |
|------------|---------|
| Semantic Kernel (for agent management) | Over-engineered for this use case. You are launching CLI processes, not orchestrating LLM-to-LLM conversations. SK is for when your platform IS an AI agent, not when it manages external ones. |
| SignalR (initially) | SSE handles all monitoring use cases. Only add if bi-directional interactive flows prove necessary (approval workflows, live terminal). |
| Newtonsoft.Json | Legacy. System.Text.Json is faster, smaller, AOT-friendly, and ships with .NET 10. |
| gRPC (for agent communication) | Agents are CLI tools, not gRPC services. HTTP/SSE is the right protocol for browser clients and simple agent wrappers. |
| Docker SDK for .NET | Out of scope per PROJECT.md. Incremental sandboxing starts with agent built-in sandboxing, not containers. |
| Electron / Tauri (for desktop) | MAUI Blazor Hybrid reuses Blazor components. No need for a separate desktop framework. |
| Hangfire / Quartz.NET | Session lifecycle is managed by the coordinator, not a job scheduler. These add unnecessary abstraction. |

## Installation

```bash
# Core project (Coordinator API)
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.3
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.3
dotnet add package CliWrap --version 3.10.0
dotnet add package SSH.NET --version 2025.1.0
dotnet add package ModelContextProtocol --version 1.1.0
dotnet add package ModelContextProtocol.AspNetCore --version 1.1.0
dotnet add package System.CommandLine --version 2.0.0
dotnet add package Spectre.Console --version 0.54.0
dotnet add package Polly --version 8.5.2
dotnet add package FluentValidation --version 11.11.0
dotnet add package Microsoft.Extensions.AI.Abstractions --version 10.3.0

# Aspire AppHost (orchestration)
dotnet add package Aspire.Hosting --version 13.1.2

# Aspire Service Defaults (telemetry, health, discovery)
dotnet add package Aspire.ServiceDefaults --version 13.1.2

# Testing
dotnet add package Aspire.Hosting.Testing --version 13.1.2
dotnet add package xunit --version 2.9.3
dotnet add package NSubstitute --version 5.3.0

# Dev dependencies
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.3
```

## Version Verification Notes

| Package | Verified Via | Date |
|---------|-------------|------|
| .NET 10 | Microsoft Learn, NuGet | 2026-03-08 |
| Aspire 13.1.2 | GitHub Releases, NuGet | 2026-03-08 |
| EF Core 10.0.3 | NuGet | 2026-03-08 |
| System.CommandLine 2.0.0 | NuGet (stable, shipped Nov 2025) | 2026-03-08 |
| Spectre.Console 0.54.0 | NuGet, GitHub Releases | 2026-03-08 |
| CliWrap 3.10.0 | NuGet | 2026-03-08 |
| SSH.NET 2025.1.0 | NuGet, GitHub | 2026-03-08 |
| ModelContextProtocol 1.1.0 | NuGet (GA v1.0 shipped Feb 2026) | 2026-03-08 |
| Microsoft.Extensions.AI.Abstractions 10.3.0 | NuGet | 2026-03-08 |

## Sources

- [.NET 10 What's New](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)
- [ASP.NET Core 10 Release Notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [Aspire 13 What's New](https://aspire.dev/whats-new/aspire-13/)
- [Aspire Roadmap 2025-2026](https://github.com/dotnet/aspire/discussions/10644)
- [SSE in ASP.NET Core .NET 10](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10)
- [System.CommandLine 2.0.0 on NuGet](https://www.nuget.org/packages/System.CommandLine/2.0.0)
- [System.CommandLine Stable Release Plan](https://github.com/dotnet/command-line-api/issues/2576)
- [EF Core 10 What's New](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [ModelContextProtocol 1.0 Release](https://www.dotnetramblings.com/post/05_03_2026/05_03_2026_3/)
- [MCP C# SDK on NuGet](https://www.nuget.org/packages/ModelContextProtocol/)
- [Microsoft.Extensions.AI Overview](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)
- [Microsoft Agent Framework Overview](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview)
- [CliWrap on NuGet](https://www.nuget.org/packages/CLIWrap)
- [SSH.NET on NuGet](https://www.nuget.org/packages/ssh.net/)
- [Nomad HTTP API](https://developer.hashicorp.com/nomad/api-docs)
- [Aspire Service Discovery](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)
- [Aspire Testing Overview](https://learn.microsoft.com/en-us/dotnet/aspire/testing/overview)
- [Blazor .NET 10 Changes](https://www.telerik.com/blogs/net-10-has-arrived-heres-whats-changed-blazor)
- [MAUI Blazor Hybrid Tutorial](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/tutorials/maui)
- [Spectre.Console 0.54.0 Release](https://spectreconsole.net/blog/2025-11-13-spectre-console-0-54-released)
