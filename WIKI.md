# SCRIPTORIUM Wiki

## Overview

Scriptorium is a native Windows application for organizing personal media into a unified local library.

The application manages tutorials, TV shows and movies stored on the user's computer. Metadata is stored separately from the original media files, allowing the existing folder structure to remain untouched.

The application follows a **local-first** philosophy and is designed to work completely offline.

---

# Architecture

The application follows the MVVM architectural pattern.

Responsibilities:

- **Views** — Presentation only.
- **ViewModels** — UI logic and state.
- **Services** — Business logic.
- **Repositories** — Data access.
- **Models** — Domain entities.

Business logic should never exist inside Views or code-behind files.

---

# Project Structure

Scriptorium
│
├── App
├── Views
├── ViewModels
├── Models
├── Services
├── Repositories
├── Data
├── Resources
├── Assets
└── Helpers

---

## Solution Structure

The solution is split into four projects so application dependencies point inward:

```
Scriptorium.App (WPF UI and composition root)
    -> Scriptorium.Infrastructure (data access implementations)
    -> Scriptorium.Core (domain models and service abstractions)

Scriptorium.Infrastructure
    -> Scriptorium.Core

Scriptorium.Tests
    -> Scriptorium.Core
```

| Project | Responsibility | Main namespaces/folders |
| --- | --- | --- |
| `Scriptorium.App` | WPF presentation and application composition. | `Scriptorium.App.Views`, `ViewModels`, `Resources`, `Assets`, `Helpers` |
| `Scriptorium.Core` | Domain models and business-service abstractions. It has no dependency on UI or data access. | `Scriptorium.Core.Models`, `Services` |
| `Scriptorium.Infrastructure` | Persistence and external infrastructure implementations. | `Scriptorium.Infrastructure.Data`, `Repositories` |
| `Scriptorium.Tests` | Automated tests for Core behavior; it does not reference the UI. | `Scriptorium.Tests` |

Views contain presentation only. ViewModels own UI state and coordinate Core services. Repository contracts and domain models belong in Core; their concrete data-access implementations belong in Infrastructure. `Scriptorium.App` is the only project permitted to reference both UI-facing and infrastructure layers, which keeps dependency wiring at the composition root.

The initial Tests project contains no test framework dependency because no tests have been introduced yet; add one together with the first test suite.

---

# Technology Stack

| Layer                | Technology                               |
| -------------------- | ---------------------------------------- |
| Language             | C#                                       |
| Framework            | WPF (.NET)                               |
| Architecture         | MVVM                                     |
| Database             | SQLite                                   |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| IDE                  | Visual Studio 2022                       |
| Version Control      | Git                                      |

---

# Coding Guidelines

General principles:

- Follow SOLID.
- Prefer composition over inheritance.
- Keep classes focused on a single responsibility.
- Use dependency injection whenever possible.
- Avoid unnecessary static classes.
- Prefer readability over cleverness.
- Keep methods small and focused.
- Prefer async/await for I/O.
- Avoid duplicated logic.
- Use meaningful names.

---

# UI Guidelines

Design goals:

- Dark theme
- Modern appearance
- Rounded corners
- Large media thumbnails
- Smooth animations
- Consistent spacing
- Responsive layout
- Minimal visual clutter

The UI should prioritize speed and simplicity over visual complexity.

---

# Performance Goals

The application should remain responsive even with very large libraries.

Guidelines:

- Never block the UI thread.
- Perform long-running work asynchronously.
- Cache thumbnails.
- Use efficient SQLite queries.
- Load data lazily whenever possible.

---

# Development Rules

When implementing new functionality:

1. Maintain MVVM separation.
2. Avoid business logic inside Views.
3. Reuse existing services before creating new ones.
4. Build small, incremental features.
5. Favor maintainability over premature optimization.
6. Keep the codebase simple and consistent.

---

# Definition of Done

A task is complete when:

- The feature behaves as expected.
- The solution builds without errors or warnings.
- Existing functionality remains unaffected.
- The implementation follows the project architecture.
- Documentation is updated when necessary.
- Acceptance criteria are satisfied.

# AI Development Instructions

Before implementing a task:

- Read README.md and WIKI.md.
- Follow the MVVM architecture.
- Reuse existing services when possible.
- Do not introduce unnecessary dependencies.
- Keep implementations simple and maintainable.
- Do not modify unrelated files.
- Explain important architectural decisions.

# Git Policy

Git operations are performed exclusively by the developer.

The AI must never:

- Commit
- Push
- Pull
- Merge
- Rebase
- Create or switch branches
- Stage files
- Modify Git history
