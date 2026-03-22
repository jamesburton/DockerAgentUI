---
title: Project Management & Repo Automation
priority: urgent
created: 2026-03-22
source: UAT session feedback
---

# Project Management & Repo Automation

Add/edit/delete projects to select them for sessions, allowing working in specific folders.

## Requirements

- ProjectEntity: name, git URL, default branch, local path hints
- CRUD API: POST/GET/PUT/DELETE /api/projects
- Web UI: project list page, add/edit/delete dialogs
- LaunchDialog: project selector dropdown that auto-fills repo path + branch
- Session launch: check if repo exists on target host, auto-clone if missing
- Branch selection: launch on a specific branch, or create worktree from that branch instead of main

## Context

Currently sessions require manual repo path specification or rely on host defaults. Operators need a way to manage projects centrally and have the system handle repo presence and branch selection automatically.
