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

## Local database

Scriptorium stores its local metadata in SQLite at `%LocalAppData%\Scriptorium\scriptorium.db`. The file name can be changed through the `Database:FileName` setting in `Scriptorium.App/appsettings.json`; it must remain a file name rather than a path.

At startup, `IDatabaseInitializer` applies EF Core migrations before the main window is shown, creating the database when it does not yet exist, then verifies the connection. Existing databases created before migrations are safely upgraded and baselined once. Data access code should obtain contexts through the registered `IDbContextFactory<ScriptoriumDbContext>` so operations remain short-lived and can run asynchronously.

### Current schema

The current schema keeps media metadata in the existing `MediaItems` table. `LibraryFolderId` is required and references `LibraryFolders.Id`; deleting a library folder is restricted so indexed media cannot be orphaned. `CategoryId` remains optional and references `Categories.Id`, with category deletion setting the reference to `NULL`.

`IsFavorite` is the sole favorite state. Runtime and playback position are stored as whole seconds in `RuntimeSeconds` and `PlaybackPositionSeconds`; `LastPlayed` remains the last-watched timestamp. `FileSize`, `CreatedDate`, and `ModifiedDate` support later rescans. The schema upgrader copies legacy favorite rows into `IsFavorite`, then removes the legacy `Favorites` table and obsolete TV-show-specific columns.

### Repositories

Repository contracts live in `Scriptorium.Core.Repositories`; EF Core implementations live in `Scriptorium.Infrastructure.Repositories`. `IMediaItemRepository`, `ICategoryRepository`, and `ILibraryFolderRepository` provide asynchronous CRUD operations, while media-item queries also load their folder and category. The implementations use `IDbContextFactory<ScriptoriumDbContext>`, keeping their context lifetime short and independent of the WPF UI.

Imported files are persisted through `IImportedMediaPersistenceService`. It normalizes a file path, then inserts a new `MediaItem` or updates the existing item with the same path (case-insensitive), preserving one database record per imported file.

`IPlaybackProgressService` stores position, duration, completion, and last-watched time directly on `MediaItems`. Updates use a single database statement, and completed items resume at zero rather than their finished position.

`IFavoriteService` toggles the single `IsFavorite` value on each media item, making duplicate favorite records impossible. `ICategoryService` creates, renames, deletes, and assigns the existing `Categories` records; deleting a category relies on the `CategoryId` foreign key to clear assignments safely.

### Superseded prototype schema

The SQLite schema is configured in `ScriptoriumDbContext`. Every entity has an application-assigned `Guid` primary key, except junction tables where the foreign-key pair is the key.

| Table | Primary key | Foreign keys | Purpose |
| --- | --- | --- | --- |
| `LibraryFolders` | `Id` | — | Imported source folders. `Path` is unique. |
| `MediaItems` | `Id` | `LibraryFolderId` → `LibraryFolders.Id` (optional) | Common metadata for tutorials, movies, and TV shows. `Path` is unique. |
| `Tutorials` | `Id` | `Id` → `MediaItems.Id` | Tutorial subtype table. |
| `Movies` | `Id` | `Id` → `MediaItems.Id` | Movie-specific metadata; this is a one-to-one TPT subtype table. |
| `TVShows` | `Id` | `Id` → `MediaItems.Id` | TV-show-specific metadata; this is a one-to-one TPT subtype table. |
| `Seasons` | `Id` | `TVShowId` → `TVShows.Id` | A show’s seasons; show/season number is unique. |
| `Episodes` | `Id` | `SeasonId` → `Seasons.Id` | A season’s episodes; season/episode number and file path are unique. |
| `Categories` | `Id` | — | User-defined categories. `Name` is unique. |
| `MediaItemCategories` | `MediaItemId`, `CategoryId` | → `MediaItems.Id`, → `Categories.Id` | Many-to-many category assignments. |
| `Favorites` | `MediaItemId` | → `MediaItems.Id` | Optional one-to-one favorite record, including the date favorited. |
| `PlaybackProgress` | `Id` | `MediaItemId` → `MediaItems.Id` or `EpisodeId` → `Episodes.Id` | One resumable state per media item or episode. A check constraint requires exactly one owner. |

Deleting a media item, category, TV show, season, or episode cascades only to its dependent metadata. Deleting a library folder sets its media items’ optional folder reference to `NULL`, preserving the indexed media record. The separate `Favorites` and `MediaItemCategories` tables avoid duplicating favorite or category state on `MediaItems`.

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
