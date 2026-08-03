# Stock Replenishment Request System

A small internal tool for factory floor workers to request material replenishment at their
station, and for reviewers to approve, reject, or fulfill those requests. A simple
manufacturing execution and tracking system.

Two roles, one app: **Workers** create and submit requests; **Reviewers** approve, reject, and
mark them fulfilled. Every submission triggers an external stock availability check that runs
in the background so the UI never blocks waiting on it.

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 (17.12+) or VS Code — optional, `dotnet` CLI is enough

### 1. Clone and build

```bash
git clone https://github.com/bhanuprasadtarra/StockReplenishment
cd StockReplenishment
dotnet build
```

### 2. Run the API and the Blazor app (two terminals)

The API must be started with the `https` launch profile — the Blazor app's `ApiBaseUrl` points
at the API's HTTPS port by default, and `dotnet run` without `--launch-profile` picks whichever
profile is listed first in `launchSettings.json` (which is `http`-only). Skipping this flag is
the single most common reason the UI loads but shows no data.

```bash
# Terminal 1 - API
cd StockReplenishment.Api
dotnet run --launch-profile https
```

```bash
# Terminal 2 - Blazor UI
cd StockReplenishment.Blazor
dotnet run --launch-profile https
```

### 3. Open the app

| Service | URL |
|---|---|
| Blazor UI | https://localhost:7078 |
| API (Swagger) | https://localhost:7184/swagger |

The Blazor app opens on a **role selection** screen — pick **Worker** to create/submit requests,
or **Reviewer** to approve/reject/fulfill. The in-memory database is seeded with ~20 sample
requests spread across every status, priority, and location, so there's something to look at
immediately.

If Visual Studio is your thing instead: open `StockReplenishment.slnx`, set both
`StockReplenishment.Api` and `StockReplenishment.Blazor` as startup projects (multiple startup
projects), and press F5 — VS defaults to the `https` profile for both, so this works out of the
box.

---

## Architecture Overview

Three projects, one solution. The Blazor app never talks to EF Core or the database directly —
everything goes through the API over HTTP, the same way a real external client would.

```
┌────────────────────────────────────────────────────────────────────┐
│                    Browser (Worker or Reviewer)                     │
└──────────────────────────────┬───────────────────────────────────┘
                                 │ Blazor Server (SignalR circuit)
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│                    StockReplenishment.Blazor                        │
│                                                                       │
│  Pages/                                                              │
│    RoleSelection ── Requests ── RequestDetails ── RequestCreate      │
│  Shared/                                                              │
│    RequestForm · RequestDetail · ValidationPoll                      │
│  Services/                                                            │
│    ApiClient (typed HttpClient)  ·  AppState (current role)          │
└──────────────────────────────┬───────────────────────────────────┘
                                 │ REST + JSON, over HTTPS
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│                     StockReplenishment.Api                          │
│                                                                       │
│  RequestsController                                                  │
│        │                                                              │
│        ▼                                                              │
│  IReplenishmentService (ReplenishmentService)                        │
│        │                              │                               │
│        ▼                              ▼                               │
│  AppDbContext (EF Core)        IStockValidator (StockValidator)      │
│        │                        - simulated external stock check      │
│        │                        - fired via Task.Run, doesn't block   │
│        │                          the Submit request                  │
│        ▼                                                              │
└──────────────────────────────┬───────────────────────────────────┘
                                 │
                                 ▼
                   ┌───────────────────────────┐
                   │   EF Core InMemory store    │
                   │   Requests · RequestLineItems│
                   └───────────────────────────┘
```

The background task uses its own DI scope (`IServiceScopeFactory`) rather than the request's
scoped `DbContext`, since the original scope is disposed the moment `Submit` returns. The
`ValidationPoll` Blazor component polls `GET /api/requests/{id}/validation-status` on an
interval until it sees `Completed` or `Failed`.

---

## What's Implemented

**Data model** — `ReplenishmentRequest` (1) → (many) `RequestLineItem`, covering the full
lifecycle plus workflow metadata (submitted/approved/fulfilled dates, approver, rejection
reason, validation job state). Seeded with 20 requests spanning every status, priority, and
location combination.

**Workflow** — `Draft → Submitted → Approved → Fulfilled`, with `Rejected` as an alternate
terminal branch off `Submitted`. Every transition is guarded server-side
(`ValidateStateTransition`), so illegal jumps (e.g. approving a Draft, re-submitting an Approved
request) fail with `409 Conflict` rather than silently succeeding.

**REST API** — full CRUD plus workflow actions on `/api/requests`:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/requests` | List, with `status`/`priority`/`location` filters + pagination |
| `GET` | `/api/requests/{id}` | Fetch one request |
| `POST` | `/api/requests` | Create (Draft) |
| `PUT` | `/api/requests/{id}` | Update a Draft request |
| `DELETE` | `/api/requests/{id}` | Delete a Draft request |
| `POST` | `/api/requests/{id}/submit` | Submit for approval (202, kicks off async validation) |
| `GET` | `/api/requests/{id}/validation-status` | Poll external validation result |
| `POST` | `/api/requests/{id}/approve` | Approve a Submitted request |
| `POST` | `/api/requests/{id}/reject` | Reject with a required reason |
| `POST` | `/api/requests/{id}/fulfill` | Record fulfilled quantities per line item |

Errors are centralized in `ErrorHandler` middleware, which maps domain exceptions to HTTP status
codes consistently: `KeyNotFoundException` → 404, `ArgumentException` → 400,
`InvalidOperationException` → 409, anything else → 500.

**External stock validation** — simulated via `StockValidator` (`Task.Delay(3-8s)` + an ~80%
random pass rate so the failure path is actually reachable in a demo), fired off in the
background so `Submit` returns immediately. See the sequence diagram above.

**UI** — a Blazor Server app (MudBlazor components) with:
- Role selection (Worker / Reviewer) gating what actions are available
- Request list with status/priority/location filtering and pagination
- Request creation form with dynamic line items
- Request detail view with role-appropriate action buttons (submit, approve, reject, fulfill)
- Live validation-status polling after submit

**Tests** — 28 NUnit tests across the service layer (EF Core InMemory) and controller layer
(NSubstitute mocks of `IReplenishmentService`), covering the full lifecycle, state-transition
guards, validation rules, filtering, pagination, and the non-blocking submit contract.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend API | ASP.NET Core (.NET 10), controller-based |
| Data access | Entity Framework Core (InMemory provider) |
| Object mapping | AutoMapper |
| Frontend | Blazor Server + MudBlazor |
| Testing | NUnit + NSubstitute |
| API docs | Swagger / OpenAPI (dev only) |

---

## Project Structure

```
StockReplenishment/
├── StockReplenishment.Api/
│   ├── Controllers/RequestsController.cs
│   ├── Services/ReplenishmentService.cs, StockValidator.cs
│   ├── Models/Domain/ (entities, enums)
│   ├── Models/Dto/ (RequestDto, LineItemDto, ApiResponse<T>, PaginatedResponse<T>)
│   ├── Mappings/MappingProfile.cs (AutoMapper)
│   ├── Middleware/ErrorHandler.cs
│   └── Data/AppDbContext.cs (schema + seed data)
├── StockReplenishment.Blazor/
│   ├── Components/Pages/ (RoleSelection, Requests, RequestDetails, RequestCreate)
│   ├── Components/Shared/ (RequestForm, RequestDetail, ValidationPoll)
│   └── Services/ (ApiClient, AppState)
└── StockReplenishment.Tests/
    ├── ReplenishmentServiceTests.cs
    └── RequestsControllerTests.cs
```

---

## Running Tests

```bash
dotnet test StockReplenishment.Tests
```

28 tests, no external dependencies (EF Core InMemory + mocked `IStockValidator`), typically
finish in a couple of seconds.

---

## Known Limitations

This was built to a ~4-6 hour scope, so a few things are intentionally out of scope for a
production system:

- **In-memory database** — data resets on every API restart (`EnsureCreated()` re-seeds).
- **No authentication** — role selection is a client-side convenience, not an auth boundary;
  the API trusts whatever caller hits it.
- **Blazor references the API project directly** for shared DTOs rather than a separate shared
  library, which is why you'll see a harmless `Program` type-collision build warning.
- **Location values are a fixed, hardcoded set** (`Station-A`, `Station-B`, `Warehouse-Main`,
  `Packaging`) rather than a managed lookup table.
