# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands

### Backend (.NET 10)
```bash
# Run the server (from repo root or Nuuru.Server/)
dotnet run --project Nuuru.Server

# Run tests
dotnet test Nuuru.Server.Tests

# Run a specific test
dotnet test Nuuru.Server.Tests --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Add EF Core migration (SQLite - local dev)
dotnet ef migrations add MigrationName --project Nuuru.Server

# Add EF Core migration (PostgreSQL - Docker/production)
dotnet ef migrations add MigrationName --context PostgresApplicationDbContext --output-dir Migrations/PostgreSQL --project Nuuru.Server

# Apply migrations
dotnet ef database update --project Nuuru.Server
```

### Frontend (React + Vite + Vike)
```bash
# From nuuru.client/
npm run dev      # Start dev server
npm run build    # Production build
npm run lint     # Run ESLint
```

### Full Stack Development
Run the .NET server which proxies to the Vite dev server. The frontend proxies `/api` requests to the backend at `https://localhost:7028`.

## Architecture Overview

### Backend Structure (Nuuru.Server)
- **ASP.NET Core Web API** with .NET 10, PostgreSQL via Npgsql/EF Core
- **Authentication**: JWT Bearer tokens with refresh token rotation
- **Authorization**: Custom permission-based system using claims (`Nuuru.Server/Auth/Permissions.cs`)
  - Permissions defined as constants (e.g., `Permissions.User.UploadPost`)
  - `PermissionPolicyProvider` dynamically creates authorization policies
  - Permissions stored in `permission` claims, denials in `permission.deny` claims

### Key Backend Services
| Service | Purpose |
|---------|---------|
| `PostService` | Booru post CRUD, file hashing, duplicate detection |
| `TagService` | Tag management with categories and colors |
| `CommentService` | Comments with BBCode parsing |
| `BBCodeService` | Server-side BBCode to HTML conversion |
| `AuthService` | Registration, login, token management |
| `BanService` | User bans (checked via `BanCheckMiddleware`) |
| `LocalFileStorageService` | File storage in `uploads/` directory |

### Database Models
- `ApplicationUser` / `ApplicationRole` - Identity with custom permissions
- `Post` - Image/video uploads with hash deduplication
- `Tag` / `TagCategory` - Hierarchical tag system
- `Comment` - BBCode stored as raw + HTML

### Frontend Structure (nuuru.client)
- **Vike** (file-based routing) + **React 19** + **TypeScript**
- **SWR** for data fetching and caching
- **Tailwind CSS v4** + shadcn/ui components
- Path aliases: `@/` = `src/`, `@/components`, `@/lib`, `@/hooks`

### Key Frontend Directories
- `src/pages/` - Vike routes (`+Page.tsx`, `+route.ts`)
- `src/components/ui/` - shadcn/ui primitives
- `src/components/booru/` - Gallery, post viewer, comments
- `src/lib/hooks/` - SWR hooks (`useAuth`, `usePosts`, `useComments`)
- `src/lib/api/` - Axios client with interceptors for auth
- `src/lib/bbcode/` - Client-side BBCode parser for live preview

### API Routes
All API routes prefixed with `/api`:
- `/api/auth/*` - Authentication endpoints
- `/api/booru/posts/*` - Post CRUD and file serving
- `/api/booru/posts/{id}/comments` - Comments
- `/api/booru/tags/*` - Tag management
- `/api/moderation/*` - Bans, reports

## Git Conventions

- Commits should be atomic where possible - each commit should represent a single logical change

## Testing

Tests use xUnit + FluentAssertions + Moq:
- `Nuuru.Server.Tests/Unit/` - Service unit tests with mocked dependencies
- `Nuuru.Server.Tests/Integration/` - Controller tests using `WebApplicationFactory`
- Test database uses EF Core InMemory provider
