# API examples

## Start a Nomad-style session

```json
POST /api/sessions
{
  "imageOrProfile": "dotnet-dev",
  "requirements": {
    "os": "windows",
    "executionMode": 1,
    "cpuMin": 2,
    "memMinMb": 4096,
    "labels": { "tier": "dev" }
  },
  "worktreeId": "blazorsk-main",
  "requestedSkillProfile": "default"
}
```

## Start an SSH session

```json
POST /api/sessions
{
  "imageOrProfile": "powershell-dev",
  "requirements": {
    "os": "windows",
    "executionMode": 2,
    "acceptRisk": true,
    "targetHostId": "ssh-win-dev-01"
  },
  "worktreeId": "hotfix-123",
  "reason": "accept-risk local host operation"
}
```

## Send a skill-based input

```json
POST /api/sessions/{id}/input
{
  "skillId": "dotnet.build",
  "input": "build the solution",
  "arguments": {
    "projectOrSolution": "AgentSafeEnv.sln"
  }
}
```
