---
status: awaiting_human_verify
trigger: "HostMetricPollingService fails to connect to host Strix defined as ssh://strix with SocketException No such host is known"
created: 2026-03-10T00:00:00Z
updated: 2026-03-10T00:00:00Z
---

## Current Focus

hypothesis: CONFIRMED and FIXED - ssh:// prefix stripping centralized in factory
test: Build succeeds, 66 related tests pass, no regressions
expecting: User verifies metric polling connects to Strix successfully
next_action: Await human verification

## Symptoms

expected: HostMetricPollingService connects to host Strix via SSH and polls metrics successfully
actual: SocketException (11001): No such host is known
errors: System.Net.Sockets.SocketException (11001): No such host is known. Stack trace shows it failing at SshHostConnection.ConnectAsync line 63
reproduction: Any metric polling cycle triggers the error for host Strix
started: Host works fine via terminal `ssh ssh://strix` - issue is specific to SSH.NET usage

## Eliminated

- hypothesis: SSH config Host alias not being read by SSH.NET
  evidence: No ~/.ssh/config file exists. hostname "Strix" resolves fine via DNS/hosts. The issue is the ssh:// prefix.
  timestamp: 2026-03-10

## Evidence

- timestamp: 2026-03-10
  checked: SshBackend.StartAsync line 96
  found: Does `host.Address!.Replace("ssh://", "")` before passing to connection factory
  implication: SshBackend correctly strips the ssh:// prefix

- timestamp: 2026-03-10
  checked: HostMetricPollingService.PollHostAsync line 106
  found: Passes `host.Address!` directly to `_connectionFactory.Create()` without stripping ssh://
  implication: SSH.NET receives "ssh://strix" as hostname, cannot resolve it

- timestamp: 2026-03-10
  checked: HostInventoryPollingService.PollHostAsync line 148
  found: Same bug - passes `host.Address!` directly without stripping ssh://
  implication: Both polling services have the same bug

- timestamp: 2026-03-10
  checked: Windows hostname resolution
  found: Machine hostname is "Strix", `ping strix` resolves to fe80::c148:db5:29d4:d4a
  implication: Once ssh:// is stripped, "strix" will resolve correctly

- timestamp: 2026-03-10
  checked: SshBackend.CleanupWorktreeIfNeeded line 327
  found: Also does `.Replace("ssh://", "")` - consistent with StartAsync
  implication: SshBackend is consistent but the pattern is duplicated in 4+ places

- timestamp: 2026-03-10
  checked: Build and test verification
  found: Build succeeds (0 errors), 66 SSH/metric/inventory tests pass, 4 pre-existing failures unrelated
  implication: Fix is safe, no regressions introduced

## Resolution

root_cause: HostMetricPollingService and HostInventoryPollingService pass host.Address (e.g. "ssh://strix") directly to SSH.NET without stripping the "ssh://" protocol prefix. SshBackend does strip it, but the polling services were missed. SSH.NET tries to DNS-resolve "ssh://strix" as a hostname, which fails with SocketException 11001.
fix: Centralized ssh:// prefix stripping in SshHostConnectionFactory.Create() so ALL callers benefit. Removed redundant .Replace("ssh://", "") calls from SshBackend and Program.cs endpoints.
verification: Build succeeds, 66 related tests pass with 0 failures
files_changed:
  - src/AgentHub.Orchestration/Backends/SshHostConnection.cs
  - src/AgentHub.Orchestration/Backends/SshBackend.cs
  - src/AgentHub.Service/Program.cs
