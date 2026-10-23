# Production Scheduler — Design & Build Guide

| | |
|---|---|
| **Version** | 2.0 |
| **Status** | In development. Core build complete; UI/UX refinement and pre-deployment work in progress |
| **Last updated** | 23 October 2026 |
| **Prepared by** | Adrian + Claude |
| **Audience** | Project owner and development team |

> This document describes the system **as it is currently built and operating**, plus the decisions and planned work that are still ahead. It replaces v1.0 (September 2026), which was the original pre-build design.
>
> **Section numbers are kept stable from v1.0.** Comments throughout the source code cite sections such as "§5.14" or "§7 Step 8"; those references still point at the right place. New material is added in new sections rather than renumbering old ones.
>
> Throughout, **Built** means implemented and in use, **Built, not wired** means the code exists and is tested but the live system doesn't call it, and **Planned** means decided but not yet built.

---

## Quick reference — key decisions

| Decision | Current state | Where |
|---|---|---|
| Stack | ASP.NET Core 9 (C#) · React 19 + TypeScript (Vite) · SQL Server · EF Core 9 | §1 |
| Job status model | Six stored states (scheduled → ready → approved → cut → in-production → completed); **Late** is a display-only override | §5.0 |
| Job data source | SCH files from MiTek Sapphire, picked up from watched folders. Parser verified against real files | §5.8 |
| Job total EqA | Sum of SCH member **fields 13 + 14** (SetupJig + ProcessJig). v1.0's "field 23" was wrong | §5.8 |
| Production tracking | PRD files polled per station (not yet tested against a real PRD file) | §5.9 |
| Cut signal | Folder renamed `{JobRef}__DONE`, watched in any configured path | §5.12 |
| File bundling | MME files bundled into per-job folders by the system | §5.11 |
| Scheduling | EDD sequencing + same-station precedence + setup times + daily capacity + breaks. ATCS built, not wired | §3.3 |
| Calendar | Day / Week / Month views, drag-and-drop rescheduling, Recalculate button, live "now" line | §5.3 |
| At risk / behind schedule | Computed from the schedule vs the due date and vs now | §5.2, §8.2 |
| Auth (current) | Local accounts, BCrypt, JWT (8 h), Admin / Viewer roles, session survives refresh | §8.1 |
| Auth (planned) | Office 365 / Entra ID sign-in; app roles Admin / Supervisor / Kiosk assigned by IT | §9.2 |
| Station grouping | `Stations.ComponentType`; Factories planned on top | §4.2, §9.3 |
| Plan vs actuals | `ScheduleItems` carry plan + actuals; `ScheduleSegments` hold the per-day split | §4.2 |
| Appearance | Brand colours (default `#0A5E73` / `#FDB913`) editable in Admin; light / dark / auto mode | §5.17 |

---

## What changed since v1.0

| Area | v1.0 said | Now |
|---|---|---|
| SCH member EqA | Field 23 is "Production Value (EqA)" | Field 23's meaning is unknown and it isn't read. True EqA = field 13 + field 14. Verified against three real jobs (§5.8) |
| SCH file format | Plain `;`-delimited records | Real files have four header lines, a record-type label on every line (`ScheduleJob: ` / `ScheduleEntry: `) and `"; "` delimiters (§5.8) |
| `JobMembers.LinearLengthM` | Field 14 = linear length | Removed. Field 14 is ProcessJig EqA; stored as `SetupEqA` / `ProcessEqA` |
| Multi-day jobs | One `ScheduleItems` row per job per day | One row per job placement + a `ScheduleSegments` row per day it spans |
| "At risk" | Superseded, not shown | Re-activated: scheduled finish within `AtRiskBufferMinutes` (default 24 h) of the due date |
| "Jobs late" tile | Past due date | Now means **behind its own schedule**; past-due jobs are still flagged "Late" everywhere else (§5.2) |
| Manual overrides | No drag-and-drop | Drag-and-drop on the Calendar (Admin), plus Schedule manually and Recalculate (§8.5) |
| Calendar | Day / Week | Day / Week / Month, red "now" line, break shading |
| Settings GET | Admin only | Any signed-in user (Viewers need the theme); PUT stays Admin only |
| Session | Token in memory only — refresh logs you out | Token kept in `sessionStorage`; survives refresh, ends when the tab/browser closes |
| Usernames | — | Case-sensitive at login (`admin` ≠ `Admin`) |
| Colours | Fixed palette | Two brand colours configurable; full light / dark theme (§5.17) |
| Engine | ATCS, TardinessCalc, OperatorAvail drive scheduling | Built and unit-tested, but the live scheduler uses EDD only (§3.3) |

---

## V3.0 CHANGES TO IMPLEMENT

| NEW WORKFLOW MODEL FOR STORED MPS & SCH FILES | DESIGN PROGRAM DUMPS IT INTO A PRODUCT POOL FOLDER (THIS FOLDER WILL BE THE REFERENCE THAT THE STATIONS WILL REFERENCE AS THE PRODUCT) --> SCH READS IN AND THEN ARCHIVES / MPS MOVES TO DESIGNATED STATION DAY BEFORE PRODUCTION IS SCHEDULED TO START --> MPS ARCHIVES WHEN JOB IS DELETED BY USER OR IS NOTICED AS COMPLETED BY SYSTEM AT END OF SHIFT|
| AN EMAIL SYSTEM THAT EMAILS THREE REPORTS TO THREE GROUPS | EMAILS REQUIRED: JOBS SCHEDULED FOR THE NEXT DAY, JOBS REQUIRED FOR "READY FOR PRODUCTION" & DAILY PRODUCTION VALUES | 
| NEW WORKFLOW MODEL FOR MME (SAW FILES) | DESIGN PROGRAM DUMPS FILES INTO A SPECIFIED FOLDER (TO BE PART OF ADMIN SETUP) --> BACKGROUND PROCESS (PART OF THIS PROGRAMS SERVER PROCESS) BUNDLES THE MME FILES INTO A NEWLY NAMED FOLDER (DETEMINED BY THE PROGRAM) --> MOVES NEWLY CREATED FOLDER INTO A SPECIFIED WAITING FOLDER (TO BE PART OF ADMIN SETUP) --> WHEN THE JOB IS READY TO BE CUT (DETERMINED BY A LOGIC SERIES (TBC)) IT IS MOVED INTO A SPECIFIED CUTTING FOLDER (TO BE PART OF ADMIN SETUP) |
| PRODUCTS WILL NOT DETERMINED BY THE SCH FILE (AS IT IS CURRENTLY), THEY WILL NOW BE DETERMINED BY THE POOL FOLDER THEY ARE DROPPED IN. |
| PRODUCTS AND THEIR RESPECTIVELY SPECIFIED POOL FOLDER WILL BE SETUP IN ADMIN |
| LIGHT/DARK/AUTO BUTTON TO BE MOVED NEXT TO THE RIGHT OF THE USER NAME |
| "CHANGE PASSWORD" BUTTON TO BE REMOVED OFF THE HEADER |  
| CALENDAR TO IMPLEMENT A DRAG&DROP FEATURE FOR THE JOBS IN CALENDAR | JOBS INFORMATION TO UPDATE AUTOMATICALLY BASED ON MOVEMENT |

---

## 1. Project Overview

A **custom web-based production scheduler** built to replace the factory's legacy system (OptiFlow / PaperlessJig scheduling). It is a bespoke solution for this factory, not an off-the-shelf tool.

**Stack:** ASP.NET Core 9 / C# API · React 19 + TypeScript front end (built with Vite) · SQL Server (LocalDB in development) · Entity Framework Core 9.

### 1.1 Where the project stands (October 2026)

| Area | State |
|---|---|
| All 8 build steps of §7 | Done |
| Hardening + gap-closing passes | Done (multi-day segments, shift breaks, station/shift/job-type editors, Schedule manually, notification badge) |
| SCH import | Verified against three real Sapphire files (roof trusses, floor trusses, wall frames) |
| UI/UX round (Oct 2026) | Done: appearance settings, dark mode, Calendar views + drag-and-drop + Recalculate + now line, Dashboard risk tiles with actions, login improvements |
| Automated tests | 136 passing (unit + end-to-end integration against a real SQL Server database) |
| PRD reader | Built and tested with synthetic files; **not yet tested against a real PaperlessJig file** |
| Deployment | Not started (§9.5) |

### 1.2 Repository layout

```
src/
  ProductionScheduler.Domain          entities, SiteClock
  ProductionScheduler.Infrastructure  EF Core DbContext, configurations, migrations, auth (BCrypt, JWT), seeder
  ProductionScheduler.Scheduling      scheduling engine, constraint resolution, background services, status logic
  ProductionScheduler.Api             ASP.NET controllers, DTOs, Program.cs
tests/
  ProductionScheduler.Tests           unit tests + Integration/ (WebApplicationFactory against a real SQL DB)
client/                               React front end (Vite)
docs/                                 this document
real_data/                            three real Sapphire .sch files used by the parser tests
scripts/reset-dev-database.ps1        drops and re-migrates the dev database
.claude/launch.json                   launch configuration for the two dev servers
```

---

## 2. Problem Definition

### Scheduling type
Multiple machines / parallel lines — jobs run concurrently across many stations (16+ at the factory).

### Primary optimisation goal
Meet due dates and minimise tardiness.

### Constraints

| Constraint | Status |
|---|---|
| Setup / changeover times | **Built, limited.** Looked up by `(FromComponentType, ToComponentType)` and added between consecutive jobs on a station. Since a station only runs one component type, a changeover between *different* types never arises in practice. No admin screen — rows are entered directly in the `SetupTimes` table |
| Job precedence | **Built, same station only.** Enforced when both jobs land on the same station. No admin screen yet (`Precedences` table) |
| Daily station capacity | **Built.** `Stations.DailyEqATarget`, overridable per day (`StationCapacityOverrides`) |
| Working hours, days, breaks | **Built.** Settings hours + working days; breaks from active shifts |
| Operator availability | **Built, not wired.** `OperatorAvail` exists; operators aren't assigned to work (`ScheduleItems.OperatorId` is always empty) |

### Machine assignment
A mix of fixed and flexible. A job pinned by an admin ("Schedule manually" or a Calendar drag) stays on its chosen station and date. Every other job is routed to the least-loaded active station whose `ComponentType` matches the job's.

---

## 3. Architecture — Four Layers

```
┌─────────────────────────────────────────────────────┐
│  Domain (ProductionScheduler.Domain)                │
│  Job · JobMember · Station · ScheduleItem ·         │
│  ScheduleSegment · Shift · ShiftBreak · Operator …  │
└──────────────────────────┬──────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────┐
│  Constraint Resolution (Scheduling/Constraint…)     │
│  SetupResolver · PrecedenceGraph · OperatorAvail*   │
└──────────────────────────┬──────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────┐
│  Scheduling Engine (Scheduling/Engine + services)   │
│  MachineAssigner · Sequencer (EDD; ATCS*) ·         │
│  TardinessCalc* · WorkingCalendar ·                 │
│  ScheduleGenerationService · JobStatusService       │
└──────────────────────────┬──────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────┐
│  ASP.NET Core API (ProductionScheduler.Api)         │
│  15 controllers — see §3.4                          │
└─────────────────────────────────────────────────────┘
  * built and unit-tested, not used by the live scheduler
```

Five background services run inside the API process (§5.8–§5.13).

### 3.1 Domain Models

The domain is the database model — see §4.2 for every entity and column. The main ones:

| Entity | Purpose |
|---|---|
| `Job` | One per job reference; SCH header fields, scheduler output, production totals, status |
| `JobMember` | One per frame member / truss in the job (SCH ScheduleEntry) |
| `Station` | A production line; `ComponentType` decides which jobs it can run |
| `ScheduleItem` | One placement of a job on a station: plan + actuals + creep flags |
| `ScheduleSegment` | The part of a placement that falls on one working day |
| `Shift` / `ShiftBreak` | Break windows (the working window itself comes from Settings — §5.18) |

### 3.2 Constraint Resolution

| Component | State | Notes |
|---|---|---|
| `SetupResolver` | Built | Looks up `SetupTimes`; returns 0 when no row exists (no changeover) |
| `PrecedenceGraph` | Built | Builds a DAG, Kahn's topological sort with EDD tie-breaking, throws on cycles. Applied per station |
| `OperatorAvail` | Built, not wired | Validates a slot against a shift window and breaks. Unit-tested; not called by the scheduler |

### 3.3 Scheduling Engine

**How a schedule run works today** (`ScheduleGenerationService`):

1. **In-flight work first.** Jobs that are `cut` or `in-production` are never re-planned. Each one occupies its station until its projected end:
   - creep flagged and not yet approved → the original planned end (frozen);
   - otherwise → the remaining EqA (`TotalEqA − CompletedEqA`) placed from now.
   Their projected end updates `Jobs.EstimatedCompletionDate`. Their existing `ScheduleItems` row is not edited, so plan-vs-actual history survives (see the known issue in §9.4).
2. **Queued work.** Jobs with `DesignStatus = FinalDesign` and status `ready` or `approved` are routed by `MachineAssigner`: pinned jobs go to their chosen station, everything else to the least-loaded matching station. Jobs with no matching active station are skipped and counted.
3. **Sequence per station.** Earliest due date first (`EstBookedDeliveryDate`, else `ExpectedDeliveryDate`, else now + 14 days). If precedence edges exist between jobs on that station, a topological sort with EDD tie-breaks is used instead.
4. **Place on the working calendar.** Each job's duration is `TotalEqA × MinsPerUnit` (from the `JobTypes` row matching its component type; 1 min/EqA if none). `WorkingCalendar` lays the work into the working window, skipping non-working days and breaks, and respecting the station's daily EqA capacity. A changeover from `SetupTimes` (previous job's component type → next job's) is added between consecutive jobs. Because each station runs a single component type, only same-type rows (e.g. `RoofTrusses → RoofTrusses`) can currently have any effect. A pinned job starts at its pinned date and doesn't push the others.
5. **Write the plan.** Not-yet-started `ScheduleItems` for each queued job are replaced with a fresh row plus one `ScheduleSegment` per day. `AssignedStationId`, `EstimatedStartDate` and `EstimatedCompletionDate` are written back to the job.

**When it runs:** after SCH files are imported (once per poll cycle), after any job create / edit / delete / pin / unpin, and when an admin presses **Recalculate** on the Calendar.

| Component | State |
|---|---|
| `MachineAssigner` | Built — fixed first, then lowest-load compatible station |
| `Sequencer.SequenceEdd` | Built and used |
| `Sequencer.SequenceAtcs` | Built, not wired (unit-tested) |
| `TardinessCalc` (total weighted, max, late count) | Built, not wired — no local-search loop compares schedules yet |
| `WorkingCalendar` | Built — window, working days, breaks, capacity, per-day segments |

### 3.4 ASP.NET API Controllers

Every endpoint requires a valid JWT unless marked Public (a fallback policy enforces this). Unauthenticated requests get `401`; a Viewer calling an Admin endpoint gets `403`. "Any" means any signed-in user.

**AuthController** — `/api/auth`

| Method | Endpoint | Role | Description |
|---|---|---|---|
| `POST` | `/login` | Public | `{ username, password }` → JWT + username + role. Username match is case-sensitive |
| `POST` | `/logout` | Any | No server-side state; the client discards the token |
| `PUT` | `/change-password` | Any | Change own password (minimum 8 characters) |

**JobsController** — `/api/jobs`

| Method | Endpoint | Role | Description |
|---|---|---|---|
| `GET` | `/` | Any | All non-archived jobs, with `isLate`, `isAtRisk`, `isBehindSchedule` computed |
| `GET` | `/{id}` | Any | Job detail |
| `GET` | `/status-refresh` | Any | Called once on app load; marks jobs whose members are all done as `completed` |
| `POST` | `/` | Admin | Create a job manually (API only — no button in the UI), then re-run the scheduler |
| `PUT` | `/{id}` | Admin | Edit job fields, re-run scheduler, return job + schedule result |
| `DELETE` | `/{id}` | Admin | Delete job, re-run scheduler |
| `POST` | `/{jobRef}/approve-for-cutting` | Admin | Move bundle folder DO NOT CUT → for Cutting; status `ready` → `approved` |
| `POST` | `/{jobRef}/schedule-manually` | Admin | Pin to `{ stationId, scheduleDate }` (component type must match); re-run scheduler |
| `DELETE` | `/{jobRef}/schedule-manually` | Admin | Unpin; re-run scheduler |

**ScheduleController** — `/api/schedule`

| Method | Endpoint | Role | Description |
|---|---|---|---|
| `GET` | `/current` | Any | All current schedule items with their per-day segments (Calendar data) |
| `GET` | `/calendar-settings` | Any | Working window, working days and breaks for drawing the Calendar |
| `POST` | `/generate` | Admin | Run the scheduler (the Calendar's Recalculate button) |
| `GET` | `/export` | Any | CSV; optional `stationId`, `dateFrom`, `dateTo` |

**Other controllers**

| Method | Endpoint | Role | Description |
|---|---|---|---|
| `POST` | `/api/schedule-items/{id}/approve-creep` | Admin | Approve detected creep |
| `GET` | `/api/stations`, `/api/stations/{id}` | Any | Stations |
| `POST/PUT/DELETE` | `/api/stations[/{id}]` | Admin | Manage stations |
| `GET` | `/api/stations/{id}/capacity-overrides` | Any | Per-day EqA overrides |
| `POST/PUT/DELETE` | `/api/stations/{id}/capacity-overrides[/{overrideId}]` | Admin | Manage overrides |
| `GET` | `/api/jobtypes[/{id}]` | Any | Job types (`MinsPerUnit`) |
| `POST/PUT/DELETE` | `/api/jobtypes[/{id}]` | Admin | Manage job types |
| `GET/POST/PUT/DELETE` | `/api/shifts[/{id}]` | Admin | Shifts and their breaks |
| `GET/POST/PUT/DELETE` | `/api/operators[/{id}]` | Admin | Operators (no UI screen yet) |
| `GET/POST/PUT/DELETE` | `/api/users[/{id}]`, `PUT /api/users/{id}/reset-password` | Admin | Local user accounts |
| `GET` | `/api/settings` | Any | All Settings rows (Viewers need the theme keys) |
| `PUT` | `/api/settings/{key}` | Admin | Update one setting |
| `GET/POST/PUT/DELETE` | `/api/admin/sch-import-paths[/{id}]` | Admin | SCH watch folders |
| `GET/POST/PUT/DELETE` | `/api/admin/batchcut-watch-paths[/{id}]` | Admin | Batchcut watch folders |
| `GET` | `/api/admin/batchcut-events?take=50` | Admin | Batchcut log |
| `POST` | `/api/quotes/estimate` | Any | Quote estimator (§5.5) |
| `GET` | `/api/reports/production?dateFrom&dateTo` | Any | Daily EqA by station, from actual member completions |
| `GET` | `/api/notifications?since=` | Any | New-job and error counts for the top-bar badge |
| `GET` | `/api/maintenance-logs?take=5` | Any | Recent maintenance / import log entries |

Swagger is not used; the OpenAPI document is served at `/openapi/v1.json` in Development only.

---

## 4. Database Design

### 4.1 Decision: SQL Server

- Data is deeply relational — jobs reference stations, members belong to jobs, PRD state tracks per station per day.
- ACID transactions matter — SCH import and schedule generation must commit atomically.
- Query patterns are aggregation-heavy (dashboard, reports).
- Development uses SQL Server LocalDB (`(localdb)\MSSQLLocalDB`, database `ProductionScheduler`); integration tests use a separate `ProductionSchedulerIntegration` database.

The Calendar is drawn straight from `ScheduleItems` + `ScheduleSegments`; there is no cached schedule payload.

**Migrations** (EF Core, applied automatically on start in Development):

| Migration | What it did |
|---|---|
| `InitialCreate` (10 Sep 2026) | Full schema from v1.0 |
| `AddScheduleSegments` (20 Sep 2026) | Per-day split of multi-day placements |
| `RenameLinearLengthToEqaComponents` (30 Sep 2026) | Dropped `JobMembers.LinearLengthM`; added `SetupEqA`, `ProcessEqA` |

### 4.2 Full schema (current)

#### Auth & configuration

```sql
Users (
  Id              INT           IDENTITY PRIMARY KEY,
  Username        NVARCHAR(100) NOT NULL UNIQUE,   -- unique index is case-insensitive; login check is case-sensitive
  PasswordHash    NVARCHAR(256) NOT NULL,          -- BCrypt
  Role            NVARCHAR(20)  NOT NULL,          -- 'Admin' | 'Viewer'
  IsActive        BIT           NOT NULL,
  CreatedAt       DATETIME2     NOT NULL,
  LastLoginAt     DATETIME2     NULL
)

Settings (
  [Key]           NVARCHAR(100) PRIMARY KEY,
  Value           NVARCHAR(500) NOT NULL,
  UpdatedAt       DATETIME2     NOT NULL
)
```

#### Station structure

```sql
-- ComponentType groups stations by what they make (e.g. 'RoofTrusses', 'PosiTrusses', 'WallFrames').
-- It must match the ComponentType values in SCH files exactly.
Stations (
  Id              INT           IDENTITY PRIMARY KEY,
  Name            NVARCHAR(100) NOT NULL,
  ComponentType   NVARCHAR(100) NOT NULL,
  UncPath         NVARCHAR(500) NULL,       -- station's DataIn folder: PRD files read here, MPS files delivered here
  IsActive        BIT           NOT NULL,
  DisplayOrder    INT           NOT NULL,   -- Calendar column order
  DailyEqATarget  DECIMAL(10,4) NULL,       -- daily capacity; NULL = unlimited
  ArchivePath     NVARCHAR(500) NULL        -- where finished MPS files are archived (Nightly Task 4)
)

-- Replaces DailyEqATarget for one station on one date.
StationCapacityOverrides (
  Id              INT           IDENTITY PRIMARY KEY,
  StationId       INT           NOT NULL REFERENCES Stations(Id) ON DELETE CASCADE,
  OverrideDate    DATE          NOT NULL,
  EqATarget       DECIMAL(10,4) NOT NULL,
  CreatedAt       DATETIME2     NOT NULL,
  UNIQUE (StationId, OverrideDate)
)
```

#### Workforce

```sql
Shifts (
  Id              INT           IDENTITY PRIMARY KEY,
  Name            NVARCHAR(100) NOT NULL,
  StartTime       TIME          NOT NULL,   -- stored and editable, but not used to shape the working window (§5.18)
  EndTime         TIME          NOT NULL,
  IsActive        BIT           NOT NULL
)

ShiftBreaks (
  Id                  INT   IDENTITY PRIMARY KEY,
  ShiftId             INT   NOT NULL REFERENCES Shifts(Id) ON DELETE CASCADE,
  BreakStart          TIME  NOT NULL,
  BreakDurationMins   INT   NOT NULL
)

Operators (                                -- API exists; not yet used by the scheduler or UI
  Id              INT           IDENTITY PRIMARY KEY,
  Name            NVARCHAR(200) NOT NULL,
  ShiftId         INT           NULL REFERENCES Shifts(Id),
  IsActive        BIT           NOT NULL
)
```

#### Jobs & members

```sql
Jobs (
  Id                      INT             IDENTITY PRIMARY KEY,
  JobRef                  NVARCHAR(50)    NOT NULL UNIQUE,   -- e.g. '60656MTG'

  -- From the SCH ScheduleJob record (§5.8)
  ComponentType           NVARCHAR(100)   NULL,
  ClientName              NVARCHAR(200)   NULL,
  ExpectedDeliveryDate    DATE            NULL,
  EstBookedDeliveryDate   DATE            NULL,   -- THE due date used by scheduling, Late and At risk
  ClientRefNumber         NVARCHAR(100)   NULL,
  SiteAddress             NVARCHAR(300)   NULL,
  SiteSuburb              NVARCHAR(100)   NULL,
  SupervisorName          NVARCHAR(200)   NULL,   -- manual entry; never overwritten by re-import
  SupervisorNumber        NVARCHAR(50)    NULL,   -- manual entry; never overwritten by re-import
  JobOrderDate            DATE            NULL,   -- written once on first import
  JobOrderTime            TIME            NULL,   -- written once on first import
  DesignStatus            NVARCHAR(20)    NOT NULL,   -- 'Quote' | 'FinalDesign'
  Raw2, Raw7, Raw8        NVARCHAR(50)    NULL,   -- unknown MiTek fields: stored, never shown

  -- Scheduler output
  AssignedStationId       INT             NULL REFERENCES Stations(Id),
  EstimatedStartDate      DATETIME2       NULL,
  EstimatedCompletionDate DATETIME2       NULL,

  -- Production totals
  TotalEqA                DECIMAL(10,4)   NULL,   -- sum of member EqaValue at import
  CompletedEqA            DECIMAL(10,4)   NOT NULL,   -- running total from PRD events

  -- Cut approval
  ApprovedForCuttingAt    DATETIME2       NULL,
  ApprovedByUserId        INT             NULL REFERENCES Users(Id),

  -- File paths used by the nightly tasks
  SchSourcePath           NVARCHAR(500)   NULL,   -- final location of the processed .sch file
  MpsSourcePath           NVARCHAR(500)   NULL,   -- set to RawFilesPath on import (§5.13)

  -- Manual pin (Schedule manually / Calendar drag)
  IsManuallyScheduled     BIT             NOT NULL,
  ManualStationId         INT             NULL REFERENCES Stations(Id),
  ManualScheduleDate      DATE            NULL,

  -- Manual entry (edit modal only)
  BookedDeliveryDate      DATE            NULL,
  InvoiceNumber           NVARCHAR(100)   NULL,
  Delivered               BIT             NOT NULL,
  DeliveredAt             DATETIME2       NULL,

  -- Nightly task tracking
  SchDeletedAt            DATETIME2       NULL,
  MpsArchivedAt           DATETIME2       NULL,

  -- System
  Status                  NVARCHAR(20)    NOT NULL,   -- §5.0
  CreatedAt, UpdatedAt    DATETIME2       NOT NULL,
  IsArchived              BIT             NOT NULL,
  ArchivedAt              DATETIME2       NULL
)

JobMembers (
  Id                INT             IDENTITY PRIMARY KEY,
  JobRef            NVARCHAR(50)    NOT NULL REFERENCES Jobs(JobRef) ON DELETE CASCADE,
  MemberId          NVARCHAR(20)    NOT NULL,   -- e.g. 'E17', 'T3'
  ComponentType     NVARCHAR(100)   NULL,
  MemberCategory    NVARCHAR(100)   NULL,       -- SCH field 8, e.g. 'Ext Load Bearing'
  SetupEqA          DECIMAL(10,4)   NOT NULL,   -- SCH field 13 (SetupJig activity; 0 for single-activity types)
  ProcessEqA        DECIMAL(10,4)   NOT NULL,   -- SCH field 14 (ProcessJig activity)
  EqaValue          DECIMAL(10,4)   NOT NULL,   -- SetupEqA + ProcessEqA
  WidthMm           INT             NULL,       -- SCH field 17
  HeightMm          INT             NULL,       -- SCH field 18
  Batch             INT             NOT NULL,   -- SCH field 25
  Sequence          INT             NOT NULL,   -- SCH field 26
  SubSequence       INT             NOT NULL,   -- SCH field 27
  Done              BIT             NOT NULL,   -- set by the PRD reader
  CompletedAt       DATETIME2       NULL,
  CompletedEqA      DECIMAL(10,4)   NULL,
  Raw2              NVARCHAR(50)    NULL,
  UNIQUE (JobRef, MemberId)
)
```

#### Scheduling

```sql
SetupTimes (
  FromComponentType   NVARCHAR(100)   NOT NULL,
  ToComponentType     NVARCHAR(100)   NOT NULL,
  DurationMins        INT             NOT NULL,
  PRIMARY KEY (FromComponentType, ToComponentType)
)

Precedences (
  PredecessorJobRef   NVARCHAR(50)    NOT NULL REFERENCES Jobs(JobRef),
  SuccessorJobRef     NVARCHAR(50)    NOT NULL REFERENCES Jobs(JobRef),
  PRIMARY KEY (PredecessorJobRef, SuccessorJobRef)
)

-- One placement of a job on a station: the plan, plus actuals as production happens.
ScheduleItems (
  Id                    INT             IDENTITY PRIMARY KEY,
  JobRef                NVARCHAR(50)    NOT NULL REFERENCES Jobs(JobRef) ON DELETE CASCADE,
  StationId             INT             NOT NULL REFERENCES Stations(Id),
  OperatorId            INT             NULL REFERENCES Operators(Id),   -- always NULL today
  ScheduleDate          DATE            NOT NULL,   -- day the placement starts
  PlannedStart          DATETIME2       NOT NULL,
  PlannedEnd            DATETIME2       NOT NULL,
  PlannedEqA            DECIMAL(10,4)   NOT NULL,
  MembersTotal          INT             NOT NULL,
  ActualStart           DATETIME2       NULL,       -- first Trusses Made event
  ActualEnd             DATETIME2       NULL,
  EqaCompleted          DECIMAL(10,4)   NOT NULL,
  MembersCompleted      INT             NOT NULL,
  CreepDetected         BIT             NOT NULL,
  CreepDetectedAt       DATETIME2       NULL,
  CreepApprovedAt       DATETIME2       NULL,
  CreepApprovedByUserId INT             NULL REFERENCES Users(Id),
  GeneratedAt           DATETIME2       NOT NULL,
  IsArchived            BIT             NOT NULL,
  ArchivedAt            DATETIME2       NULL,
  UNIQUE (JobRef, ScheduleDate)
)

-- The part of a placement that falls on one working day. A two-and-a-half-day job has three rows.
ScheduleSegments (
  Id              INT             IDENTITY PRIMARY KEY,
  ScheduleItemId  INT             NOT NULL REFERENCES ScheduleItems(Id) ON DELETE CASCADE,
  SegmentDate     DATE            NOT NULL,
  SegmentStart    DATETIME2       NOT NULL,
  SegmentEnd      DATETIME2       NOT NULL,
  PlannedEqA      DECIMAL(10,4)   NOT NULL
)
```

#### Background-service configuration and logs

```sql
SchImportPaths (
  Id              INT             IDENTITY PRIMARY KEY,
  Name            NVARCHAR(100)   NOT NULL,
  UncPath         NVARCHAR(500)   NOT NULL,
  ComponentType   NVARCHAR(100)   NULL,   -- NULL = accept any job type
  IsActive        BIT             NOT NULL
)

PrdReadState (
  StationId       INT       NOT NULL REFERENCES Stations(Id),
  FileDate        DATE      NOT NULL,
  LastLineRead    INT       NOT NULL,
  LastProcessedAt DATETIME2 NOT NULL,
  PRIMARY KEY (StationId, FileDate)
)

BatchcutWatchPaths (
  Id              INT           IDENTITY PRIMARY KEY,
  Name            NVARCHAR(100) NOT NULL,
  UncPath         NVARCHAR(500) NOT NULL,
  IsActive        BIT           NOT NULL
)

BatchcutEvents (
  Id              INT           IDENTITY PRIMARY KEY,
  BatchcutPathId  INT           NOT NULL REFERENCES BatchcutWatchPaths(Id),
  FolderName      NVARCHAR(200) NOT NULL,   -- e.g. '59778MWF__DONE'
  JobRef          NVARCHAR(50)  NULL,
  DetectedAt      DATETIME2     NOT NULL,
  Status          NVARCHAR(20)  NOT NULL,   -- 'applied' | 'duplicate' | 'error'
  ErrorMessage    NVARCHAR(500) NULL,
  IsArchived      BIT           NOT NULL,
  ArchivedAt      DATETIME2     NULL
)

-- Audit log for anything that runs unattended.
MaintenanceLogs (
  Id              INT             IDENTITY PRIMARY KEY,
  TaskName        NVARCHAR(100)   NOT NULL,   -- 'SchImport' | 'PrdRead' | 'MmeBundle' | 'MpsToDataIn' | 'SawFilesCleanup'
                                              -- | 'SchDelete' | 'MpsArchive' | 'RetentionArchive' …
  JobRef          NVARCHAR(50)    NULL,
  SourcePath      NVARCHAR(500)   NULL,
  DestPath        NVARCHAR(500)   NULL,
  Outcome         NVARCHAR(20)    NOT NULL,   -- 'success' | 'skipped' | 'error'
  Message         NVARCHAR(500)   NULL,
  RunAt           DATETIME2       NOT NULL,
  IsArchived      BIT             NOT NULL,
  ArchivedAt      DATETIME2       NULL
)

JobTypes (
  Id              INT             IDENTITY PRIMARY KEY,
  Name            NVARCHAR(100)   NOT NULL,
  ComponentType   NVARCHAR(100)   NULL,   -- links to Stations.ComponentType
  MinsPerUnit     DECIMAL(8,2)    NOT NULL,   -- minutes per EqA: drives BOTH scheduling durations and quotes
  IsActive        BIT             NOT NULL
)
```

### 4.3 Key indexes

| Index | Columns | Purpose |
|---|---|---|
| `IX_JobMembers_JobRef` + unique `UQ_JobMembers_Ref` | `(JobRef)`, `(JobRef, MemberId)` | PRD member matching |
| `IX_ScheduleItems_Date_Station` | `(ScheduleDate, StationId)` | Calendar queries |
| `UQ_ScheduleItems_Job_Date` | `(JobRef, ScheduleDate)` unique | One placement per job per start day |
| `IX_ScheduleItems_Creep` | `(CreepDetected, CreepApprovedAt)` | Creep alerts |
| `IX_ScheduleSegments_Date`, `IX_ScheduleSegments_ScheduleItemId` | | Calendar day lookups |
| `IX_Jobs_Status` | `(Status, DesignStatus, IsArchived)` | Status refresh / queue |
| `IX_Jobs_Station` | `(AssignedStationId, DesignStatus, IsArchived)` | Dashboard station cards |
| `IX_Jobs_ManualSchedule` | `(IsManuallyScheduled, ManualScheduleDate)` | Pinned jobs |
| `IX_Stations_ComponentType` | `(ComponentType, IsActive)` | Routing |
| `IX_BatchcutEvents_JobRef` | `(JobRef, Status)` | Batchcut dedupe |
| `IX_PrdReadState_Station_Date` | `(StationId, FileDate)` | PRD resume point |
| `IX_StationCapacityOverrides_Station_Date` | `(StationId, OverrideDate)` unique | Capacity lookups |
| `IX_Users_Username` | `(Username)` unique | Login |

### 4.4 Key rules and where they're enforced

| Rule | Enforcement |
|---|---|
| Job reference unique | `UNIQUE` on `Jobs.JobRef` |
| Member ID unique within a job | `UNIQUE (JobRef, MemberId)` |
| Usernames unique regardless of case; login requires exact case | Case-insensitive collation on the unique index + ordinal re-check in `AuthController.Login` |
| A job only runs on a station with its `ComponentType` | `MachineAssigner`; also validated by Schedule manually / drag-and-drop |
| A job cannot precede itself / no cycles | `PrecedenceGraph` throws on cycles |
| `JobOrderDate` / `JobOrderTime` never overwritten | SCH import |
| Manually entered fields never overwritten by re-import (`SupervisorName`, `SupervisorNumber`, `BookedDeliveryDate`, `InvoiceNumber`, `Delivered`) | SCH import |
| Production-tracking fields on members (`Done`, `CompletedAt`, `CompletedEqA`) never overwritten by re-import | SCH import |
| A pinned job is never moved by automatic scheduling | Scheduler places it at its pin date |
| Cut / in-production jobs are never re-planned | Scheduler (in-flight pass) |
| Creep-flagged jobs hold their plan until approved | Scheduler uses the frozen planned end until `CreepApprovedAt` is set |
| An SCH file from a component-scoped import path must match that type | `SchImportService` rejects it to `_errors` |
| A DONE folder with no matching job, or for a job not yet `approved`, is an error, not silently dropped | `BatchcutEvents.Status = 'error'`; shows in the notification badge |
| A DONE folder already processed is never reprocessed | `BatchcutEvents` is the dedupe store per `(path, folder)` |
| Nothing is hard-deleted by retention | Nightly Task 5 only sets `IsArchived` |

### 4.5 Settings table seed values

All keys are created on API start-up if missing (`DatabaseSeeder`); existing values are never overwritten. See §6.2 for the full table.

---

## 5. GUI Design

### 5.0 Colour palette and job statuses

#### Brand colours

The two brand colours are now **settings**, not constants (§5.17). Defaults:

| Role | Default | Usage |
|---|---|---|
| Primary | `#0A5E73` (teal) | Sidebar, primary buttons, active tabs, column headers |
| Accent / secondary | `#FDB913` (amber) | New-job badge, active nav item, Scheduled status |

Hover shades, the sidebar background and badge tints are derived from these two automatically, so changing them in Admin re-themes the whole app.

#### Job statuses

`Jobs.Status` stores one of six values. **Late is not stored** — it is worked out whenever the jobs are read: a job is Late when its due date (`EstBookedDeliveryDate`, else `ExpectedDeliveryDate`) is before today and it isn't completed. Storing `late` would erase the workflow state the background services depend on.

| Stored status | Label shown | Set by | Light-mode colours (block bg / border / text) |
|---|---|---|---|
| `scheduled` | Scheduled | SCH import of a **Quote** (or manual create of a non-final job) | `#FFFBEB` / `#FDB913` / `#78350F` |
| `ready` | Ready for production | SCH import of a **FinalDesign** | `#F0FDF4` / `#86EFAC` / `#166534` |
| `approved` | Approved | Admin: Approve for cutting (§5.11) | `#ECFEFF` / `#06B6D4` / `#0E7490` |
| `cut` | Cut and ready | Batchcut watcher sees the DONE folder (§5.12) | `#EFF6FF` / `#3B82F6` / `#1E40AF` |
| `in-production` | In production | First PRD *Trusses Made* event (§5.9) | `#DCFCE7` / `#16A34A` / `#14532D` |
| `completed` | Completed | All members done (PRD reader, or the status refresh on app load) | `#F9FAFB` / `#9CA3AF` / `#4B5563` |
| *(override)* | **Late** | Due date passed, not completed | `#FFF1F2` / `#EF4444` / `#9F1239` |

Every status colour has a dark-mode equivalent (§5.17).

Two more flags are computed alongside Late and drive the Dashboard (§5.2):

| Flag | Rule |
|---|---|
| **At risk** | Not completed, not already Late, has a scheduled finish, and finishes **0 to `AtRiskBufferMinutes`** before the end of its due date (default 1,440 = 24 h). A scheduled finish *after* the due date doesn't count as at risk |
| **Behind schedule** | Not completed and the schedule's own estimated completion is already in the past — the factory has slipped against its plan |

---

### 5.1 Application layout

```
┌───────────────┬───────────────────────────────────────────────────────────┐
│               │  Top bar: [N new jobs · N errors] [Theme] user · role     │
│  Sidebar      │           [Change password] [Log out]                     │
│               ├───────────────────────────────────────────────────────────┤
│  - Dashboard  │                                                    ┃ Side │
│  - Calendar   │  Main content area (scrollable)                    ┃ panel│
│  - Jobs       │                                                    ┃ (tab)│
│  - Quotes     │                                                    ┃      │
│  - Reports    │                                                    ┃      │
│  - Settings * │                                                    ┃      │
│  [N late]     │                                                    ┃      │
└───────────────┴───────────────────────────────────────────────────────────┘
* Settings visible to Admin only
```

- **Notification badge** (top bar): `[2 new jobs · 1 error]`, polled every 30 s. "New jobs" links to Jobs; "errors" opens the Dashboard's side panel. Each user's "last seen" point is kept per browser, so clicking a badge clears it.
- **Theme toggle** (top bar): cycles Auto → Light → Dark (§5.17).
- **"N late"** (sidebar): count of past-due jobs.
- **Change password** is available to every user.

---

### 5.2 Dashboard (landing page)

**Summary tiles**

| Tile | Counts | Notes |
|---|---|---|
| Jobs currently running | Jobs `in-production` | |
| Jobs on time | Jobs not past due and not completed | Includes at-risk jobs |
| Jobs at risk | At-risk flag (§5.0) | Hover text explains the rule |
| Jobs late | **Behind-schedule** flag (§5.0) | Clicking it filters the urgent list to behind-schedule jobs. Note: this is not the same "late" as the sidebar count, which is past due |

**Station cards** (one per active station): today's EqA done vs the station's daily target (or today's override, flagged on the card), plus the current and next job on that station. Cards show **jobs**, not individual members (§9.4).

**Urgent jobs** — every job that is past due, at risk or behind schedule, earliest due date first:

| Column | Content |
|---|---|
| Job Reference / Structure Name / Station / Due Date / Status | |
| Why | Badges: *Past due*, *Behind schedule*, *At risk* |
| Actions (Admin) | **Approve** (for `ready` jobs), **Reschedule** (opens Schedule manually), **View** (opens the job popup) |

**Side panel:** Needs attention (recent errors), Creep alerts, Active capacity overrides, Recent maintenance.

---

### 5.3 Calendar view

**Axis orientation** — columns are stations (in `DisplayOrder`, horizontally scrollable); rows are hours (Day) or days (Week). The row-label column stays visible when scrolling sideways.

**Toolbar:** ← / → navigation · Today · **Day / Week / Month** toggle · **Recalculate** (Admin).

| View | Layout |
|---|---|
| **Day** | One row per hour of the working window (default 06:00–18:00). Blocks are sized by duration. Breaks are hatched. A **red "now" line** marks the current time on today's view, refreshing every 15 minutes |
| **Week** | One row per working day; the blocks for each station-day stack vertically, showing reference, start time, duration and "day 2/3" for multi-day jobs |
| **Month** | A Mon–Sun grid of 6 weeks. Each day shows up to 4 job chips and "+N more", which jumps to that day in Day view |

**Blocks** show the status colour, a **LATE** badge for past-due jobs and a creep badge when creep is flagged. **Double-click** opens the Job Detail Popup.

**Drag-and-drop (Admin):** drag any non-completed block onto another station on another day. It only lands on a station with the same component type; anything else is refused with a message. A drop pins the job there (the same as Schedule manually) and re-runs the scheduler.

**Recalculate (Admin):** re-runs the scheduler to close gaps left by manual moves. See the in-flight display issue in §9.4.

**`+ Station`** — after the last column; opens the Add station dialog.

**Side panel:** Unscheduled ready jobs, Creep alerts, Station EqA today vs target.

Calendar data loads when the page opens and after each action; it doesn't auto-refresh (only the now line does).

---

### 5.4 Job Detail Popup

Opened by double-clicking a calendar block, or with View on the Dashboard or Jobs list. **View only.**

Fields: Job Reference, Job Structure Name, Component Type, Client Name, Site Address, Site Suburb, Supervisor Name, Supervisor Number, Job Order Date, Production Value (Eq), Estimated Start Date, Estimated Completion Date, Expected Delivery Date, Booked Delivery Date, Invoice Number, Delivered. Also shows an EqA progress bar and a "manually scheduled for …" note when pinned.

**Footer (Admin):** Approve for cutting (on `ready` jobs) · Schedule manually / Change pin · Edit. **Everyone:** Close.

**Edit modal (Admin)** can change: component type, client, delivery dates, client reference, site address and suburb, supervisor name and number, booked delivery date, invoice number, delivered. Saving re-runs the scheduler.

---

### 5.5 Jobs and Quotes (two separate pages)

**Quotes page — Quote Estimator**
- Input: quote reference + one or more lines of `Job Type` + `Qty`.
- Each line's duration is `MinsPerUnit × Qty`, placed after the latest projected completion among active jobs already queued at stations of that job type's component type. Working hours, working days and breaks are respected.
- Output per line: estimated completion date and time; with several lines, the latest is shown as "Full order ready".
- Permanently time-based: setup times and operator availability are never factored in (§8.3).

**Jobs page — Job Manager**
- Status filter chips with live counts, a table of Job Reference · Structure Name · Station · Due Date · Prod. Value · Status · Actions.
- Row actions: **View** (everyone), **Edit** and **Delete** (Admin, delete asks for confirmation).
- Side panel: filters (status, component type, due-date range) and Admin bulk actions: **Approve for cutting**, **Mark delivered**.
- Jobs arrive through SCH import. Manual creation exists in the API but has no button in the UI.

---

### 5.6 Reports view

| Report | Source | Display |
|---|---|---|
| **Production by Station** | `/api/reports/production` — EqA per station per day from when members were **actually completed**, so a three-day job spreads across its three days | Bar chart + table |
| **Jobs Completed vs Late** | Built in the browser: **Completed** = jobs whose placement finished (actual end) that day; **Late** = jobs that are *currently* past due and unfinished, counted on their due date. It does not yet count jobs that finished after their due date | Grouped bar chart + table (Date, Completed, Late, Late %) |

Controls: date range (from / to), **Export CSV** for the visible table.

---

### 5.7 Settings / Admin view

Admin only (hidden from Viewers' sidebar; the route redirects them to the Dashboard).

| Tab | What it manages |
|---|---|
| Stations | Add / edit / deactivate stations: name, component type, PRD folder, archive folder, daily EqA target, display order, per-day capacity overrides |
| Job Types | Name, component type, minutes per EqA |
| Shifts | Shift name, times and break windows |
| Users | Create users, change role (Admin / Viewer), deactivate, reset password |
| System Settings | **Appearance** card (§5.17) and every Settings key as an editable row |
| SCH Import Paths | Watch folders, optionally limited to one component type (§5.16) |
| Batchcut Watch Paths | Folders watched for DONE renames (§5.12) |
| Batchcut Log | The last 50 batchcut events with status and errors |

No screens yet for Operators, Setup Times or Precedences (§9.4).

---

### 5.8 SCH File Import

Generated by **MiTek Sapphire**. Plain text, one job per file, dropped into a watched folder. `SchImportService` polls every active `SchImportPaths` entry (§5.16).

**File naming:** `{JobRef}__{suffix}.sch`, e.g. `58749MPF__.sch`, `60656MTG__-RT.SCH` (extension matched case-insensitively).
**Dates:** `dd/mm/yyyy`.

#### Real file format (verified against three real jobs, Sep 2026)

Three things the v1.0 description missed, all confirmed on real files:

1. **Four header lines** always open the file and are skipped: `File Version=…`, `Production Units Names=…`, `Batchcut:…`, `Activities:…` (the last names the activities, e.g. `ProcessJig; SetupJig`).
2. **Every data line starts with a label**, `ScheduleJob: ` or `ScheduleEntry: `, which identifies the record type and is stripped before splitting.
3. **The delimiter is `"; "`** (semicolon + space), so every field is trimmed.

#### Import handling

- **Settle time:** a file is ignored until it has been unchanged for `SchFileSettleSeconds` (default 5), in case Sapphire is still writing it.
- **Strict:** any unrecognised line rejects the whole file with a message naming the line. A partial job with a wrong EqA total is worse than a clear error.
- **After processing**, the file is moved into a `_processed` (or `_errors`) subfolder of its import path, so it's never picked up twice. `Jobs.SchSourcePath` records the final location for Nightly Task 3.
- Each import or rejection is written to `MaintenanceLogs` (`SchImport`); rejections count as errors in the badge.
- After a poll cycle that imported anything, the scheduler runs once.

#### Two-stage workflow

```
Stage 1 — Quote (DesignStatus = "quote")
  → New job, status 'scheduled'
  → Job Order Date/Time recorded (never overwritten afterwards)
  → Not scheduled; not on the Calendar
  → A second quote for the same reference is rejected

Stage 2 — Final Design (DesignStatus = "finaldesign")
  → Existing job updated: all SCH fields except Job Order Date/Time and the manual-entry fields
  → Members refreshed (descriptive fields only; production-tracking fields untouched)
  → Status 'ready' → scheduled onto a station
  → A finaldesign with no prior quote creates the job directly
```

Not built: comparing the earliest viable production date with the Est. Booked Delivery Date at quote stage (§9.4).

#### ScheduleJob record — field map (16 fields)

| Pos | Example | Field | Behaviour |
|---|---|---|---|
| 1 | `58749MPF` | Job Reference | Unique key; also shown as Structure Name |
| 2 | | *(reserved → `Raw2`)* | Stored, never shown |
| 3 | `PosiTrusses` | Component Type | e.g. `RoofTrusses`, `PosiTrusses`, `WallFrames` |
| 4 | | Client Name | |
| 5 | `18/09/2026` | Expected Delivery Date | |
| 6 | `18/09/2026` | Est. Booked Delivery Date | **The due date** used throughout |
| 7, 8 | | *(reserved → `Raw7`, `Raw8`)* | |
| 9 | | Client Reference Number | |
| 10 | | Site Address | |
| 11 | | Site Suburb | |
| 12, 13 | *(empty)* | Supervisor Name / Number | Not from SCH; entered manually |
| 14 | `02/09/2026` | Job Order Date | Written once |
| 15 | `14:23:12` | Job Order Time | Written once |
| 16 | `FinalDesign` | Design Status | `quote` or `finaldesign` (case-insensitive) |

#### ScheduleEntry record — field map (27+ fields)

| Pos | Field | Notes |
|---|---|---|
| 1 | Job Reference | |
| 2 | *(reserved → `Raw2`)* | |
| 3 | Component Type | |
| 4 | Member ID | e.g. `E17`, `I31` |
| 8 | Member Category | e.g. `Ext Load Bearing` |
| **13** | **SetupJig EqA** | 0 for component types with a single activity (e.g. WallFrames' FramingMachine) |
| **14** | **ProcessJig EqA** | |
| 17 | Width (mm) | |
| 18 | Height (mm) | |
| 22 | Done | `False` on import |
| 23 | *(unknown — not read)* | v1.0 called this "Production Value (EqA)"; it isn't |
| 25 / 26 / 27 | Batch / Sequence / Sub-sequence | Production order |

**Total Production Value for a job = Σ (field 13 + field 14) across all members.** Verified exactly against real totals for 58749MPF (129.19) and 60656MTG (158.11), and to rounding for 60497MWG (38.63).

---

### 5.9 PRD File Reading

Written by **PaperlessJig** at each station, one file per station per day. The system only reads them.

> **Not yet verified against a real PRD file.** The reader is built from the v1.0 description and tested with synthetic files. A real file from a station is the next-best input to validate it, the same way real SCH files corrected the SCH parser.

**File naming:** `YYYYMMDD.prd` in the station's `UncPath`. **Delimiter:** comma.

**Polling:** every `PrdPollIntervalMinutes` (default 5) for each active station with a `UncPath`. Reads only lines after `PrdReadState.LastLineRead` for today's file; the file is opened with shared read/write since PaperlessJig keeps writing to it. "Today" and timestamps use the site's local time (`Site:TimeZoneId`, or the server's zone if blank).

**Events:** only `Trusses Made` (JobRef, MemberID, timestamp, qty, f2, f3, EqA) is processed; other event types are ignored. Timestamps are accepted in several day-first formats, or as a bare time combined with the file's date.

**Processing a Trusses Made event:**
1. Find the member by `(JobRef, MemberID)`. Not found → logged as *skipped* in `MaintenanceLogs`.
2. Already done → ignored (re-reading is safe).
3. Otherwise mark it done with its timestamp and EqA; add the EqA to `Jobs.CompletedEqA` and the latest `ScheduleItems.EqaCompleted` / `MembersCompleted`.
4. If the job was `cut`: status becomes `in-production` and `ScheduleItems.ActualStart` is set.
5. If every member is now done: status becomes `completed` and `ActualEnd` is set.

Unreadable lines are logged as errors (`PrdRead`) and show in the notification badge. Creep detection (§5.14) runs after every poll.

---

### 5.10 Data model implications from SCH and PRD

Superseded by the current schema in §4.2 (`JobMembers` with `SetupEqA` / `ProcessEqA`; `PrdReadState`; the full `Jobs` table).

---

### 5.11 MME File Bundler Service

`MmeFileWatcherService` replaces the old bundling program. Every `MmePollIntervalSeconds` (default 300) it scans `RawFilesPath` for `.mme` cut-list files, one per member, named `{JobRef}{MemberID}.mme`.

- **JobRef** = file name without extension, minus its last 3 characters (`59778MWFE17` → `59778MWF`).
- Files are grouped by JobRef and moved into `SawFilesDoNotCutPath\{JobRef}\`, creating the folder if needed. Later passes add any further files to the same folder; existing files are never overwritten.
- A bundle with no matching job is logged as a warning.

**Approve for cutting** (`POST /api/jobs/{jobRef}/approve-for-cutting`, Admin, job must be `ready`):
1. Both saw-file path settings must be set, and `SawFilesDoNotCutPath\{JobRef}\` must exist.
2. The folder is moved to `SawFilesForCuttingPath\{JobRef}\`.
3. Status → `approved`; `ApprovedForCuttingAt` and `ApprovedByUserId` recorded.
4. If the folder is missing or the move fails, nothing changes and the error message is shown.

The Approve button currently shows on **every** `ready` job, not only those whose bundle folder exists; a click with no folder returns a clear error (§9.4).

---

### 5.12 Batchcut Watcher Service

`BatchcutWatcherService` polls every active `BatchcutWatchPaths` entry every `BatchcutPollIntervalSeconds` (default 30) for folders the saw operators have renamed `{JobRef}__DONE`.

- Each path is checked both as-is and with this month's `\YYYY\YYYY-MM\` appended, so one table covers the "for cutting" folder and the dated "done" archive.
- **JobRef:** `59778MWF__DONE` → strip `DONE` → strip trailing `_` → `59778MWF`.
- Outcomes, each written to `BatchcutEvents`:
  - job found and `approved` → status `cut`, event **applied**;
  - job already `cut` / `in-production` / `completed` → **duplicate**, no change;
  - no matching job, or job not yet approved → **error** (shown in the notification badge and the Batchcut Log).
- A `(path, folder)` pair already in `BatchcutEvents` is never processed again.

#### Status lifecycle — who moves a job

| Actor | Trigger | Transition |
|---|---|---|
| `SchImportService` | `.sch` file imported | → `scheduled` (quote) or `ready` (finaldesign) |
| `MmeFileWatcherService` | MME files in Raw Files | None — creates the bundle folder |
| Admin | Approve for cutting | `ready` → `approved` |
| `BatchcutWatcherService` | DONE folder | `approved` → `cut` |
| `PrdReaderService` | First Trusses Made | `cut` → `in-production` |
| `PrdReaderService` / status refresh | All members done | → `completed` |

---

### 5.13 Nightly File Management Tasks

`NightlyMaintenanceService` runs once a day at `NightlyMaintenanceRunHour` (default 01:00, site time). Five independent tasks; a failure in one is logged and doesn't stop the rest. Every outcome is written to `MaintenanceLogs`.

| Task | Selects | Action |
|---|---|---|
| 1. MPS → DataIn | Jobs starting in `MpsPreMoveDaysBeforeProduction` days (default 1) with an `MpsSourcePath` and a station | Copies `{JobRef}__.MPS` to the station's `UncPath`; skips if already there |
| 2. Saw folders cleanup | DONE folders in `SawFilesForCuttingPath` | Moves them to `SawFilesDoneBasePath\YYYY\YYYY-MM\` (archival only; the status change already happened) |
| 3. Delete SCH | Completed jobs with `SchDeletedAt` empty | Deletes the file at `SchSourcePath`; sets `SchDeletedAt` |
| 4. Archive MPS | Completed jobs whose station has an `ArchivePath` and `MpsArchivedAt` empty | Moves the MPS from DataIn to the archive; already gone → logged as *skipped* |
| 5. Retention | Older than `ScheduleRetentionMonths` (default 6): **completed** jobs by last update, schedule items by generation time, batchcut events by detection time, logs by run time | Sets `IsArchived`. Never deletes |

`Jobs.MpsSourcePath` is set to `RawFilesPath` on SCH import, treating MPS files as arriving alongside the MME files. **Confirm this matches where Sapphire actually writes MPS files before go-live** (§9.5).

---

### 5.14 Job Creep Detection

Creep is when a running job's actual EqA rate falls behind its planned rate by more than `CreepThresholdPercent` (default 20 %).

**Detection** (after each PRD poll, for `in-production` placements with an actual start and no creep yet):

```
plannedRate  = PlannedEqA / (PlannedEnd − PlannedStart)     EqA per hour
expectedEqA  = plannedRate × (now − ActualStart)
creep if (expectedEqA − EqaCompleted) / expectedEqA > CreepThresholdPercent / 100
```

**When flagged:** `CreepDetected` and `CreepDetectedAt` are set, a creep badge appears on the Calendar block, and the job is listed under Creep alerts on the Dashboard and Calendar side panels. The scheduler keeps that job's original planned end until the creep is approved.

**Approving:** `POST /api/schedule-items/{id}/approve-creep` (Admin) sets `CreepApprovedAt` / `CreepApprovedByUserId`. From the next scheduler run, the job's remaining work is re-projected from now and the queue behind it moves accordingly.

Note the planned rate uses wall-clock time between planned start and end, including nights for multi-day jobs.

---

### 5.15 Collapsible Side Panel

A panel docked on the right of Dashboard, Calendar and Jobs, opened with a tab on the screen edge. Open/closed state is remembered per view in the browser; default closed.

| View | Panel content |
|---|---|
| Dashboard | Needs attention · Creep alerts · Active capacity overrides · Recent maintenance |
| Calendar | Unscheduled ready jobs · Creep alerts · Station EqA today vs target |
| Jobs | Filters (status, component type, due date range) · Admin bulk actions |

---

### 5.16 SCH Import Path Configuration

Each `SchImportPaths` row is a separately watched folder, optionally limited to one `ComponentType`. This supports a folder per product type, per building, or a catch-all alongside scoped folders.

- A file from a scoped path whose component type doesn't match is rejected into that path's `_errors` subfolder (probably dropped in the wrong folder).
- A path with no component type accepts anything.

Configured in **Admin → Settings → SCH Import Paths**.

---

### 5.17 Appearance and theming *(new)*

**Brand colours** — Admin → Settings → System Settings → **Appearance**:
- Primary and Secondary colour, each from an 8-colour palette or any custom colour (colour picker + hex).
- Stored in `ThemePrimaryColor` / `ThemeSecondaryColor`; changes apply live for everyone on their next page load.

**Light / dark mode**, two layers:

| Layer | Where | Who | Stored |
|---|---|---|---|
| Site default | Appearance → Theme mode: System / Light / Dark | Admin | `ThemeMode` setting (default `system`) |
| Personal choice | Top-bar toggle: Auto / Light / Dark | Everyone, including Viewers | That browser's `localStorage` |

- A browser that has **never used the toggle** follows the site default.
- Once the toggle is used, the choice sticks for that browser. **Auto always means "follow this computer's light/dark setting"**, even if the site default is pinned to Light or Dark.
- The personal choice also applies to the login page, so there's no light flash on start-up.

All colours come from CSS variables in `client/src/styles/theme.css`, each with a light and a dark value, so every screen, table, modal and status colour switches together.

---

### 5.18 Working calendar *(new)*

The working day used by the scheduler, the Quote Estimator and the Calendar comes from one place:

| Input | Source |
|---|---|
| Working window | `ShiftStartHour` – `ShiftEndHour` settings (default 06:00–18:00) |
| Working days | `WorkingDays` setting (0 = Sun … 6 = Sat; default Mon–Fri) |
| Breaks | All break windows from **active** shifts combined |
| Daily capacity | `Stations.DailyEqATarget`, or that day's `StationCapacityOverrides` row |

A shift's own start/end times are stored but **do not shape the working window** (§9.4).

---

### 5.19 Authentication and login screen *(new)*

- **Login form:** Enter in either field signs in, and a press-and-hold **eye icon** reveals the password only while held (it re-masks on release or when dragged off).
- **Session:** the JWT (8 hours) is held in the browser's `sessionStorage`, so a page refresh keeps you signed in; closing the tab or browser, or logging out, ends it. An expired stored token is discarded on load, and any `401` from the API returns you to the login page.
- **Usernames are case-sensitive** at login; passwords must be at least 8 characters.
- **Roles today:** Admin (everything) and Viewer (read-only: every page except Settings, no edit/approve/drag controls, can still change their own password and theme).

The planned move to Office 365 sign-in is in §9.2.

---

## 6. Configuration — Workplace-Specific

### 6.1 How configuration is stored

Anything an administrator may need to change is a row in the `Settings` table, edited in Admin → Settings → System Settings. `appsettings.json` holds only infrastructure settings: the database connection string, JWT settings, logging and `Site:TimeZoneId`.

### 6.2 Settings table keys

| Key | Purpose | Default |
|---|---|---|
| `AtRiskBufferMinutes` | At-risk window before the due date (§5.0) | `1440` (24 h) |
| `ShiftStartHour` / `ShiftEndHour` | Working window, 24-hour integers | `6` / `18` |
| `WorkingDays` | Comma-separated, 0 = Sun … 6 = Sat | `1,2,3,4,5` |
| `ScheduleRetentionMonths` | Age before records are archived (Task 5) | `6` |
| `PrdPollIntervalMinutes` | PRD reader poll | `5` |
| `BatchcutPollIntervalSeconds` | Batchcut watcher poll | `30` |
| `MmePollIntervalSeconds` | MME bundler poll | `300` |
| `SchFileSettleSeconds` | Wait before reading a new .sch file | `5` |
| `RawFilesPath` | Where Sapphire drops MME (and, assumed, MPS) files | empty — set at install |
| `SawFilesDoNotCutPath` | Bundles awaiting approval | empty — set at install |
| `SawFilesForCuttingPath` | Approved bundles for the saw | empty — set at install |
| `SawFilesDoneBasePath` | DONE archive base (`YYYY\YYYY-MM` added) | empty — set at install |
| `CreepThresholdPercent` | Creep trigger (§5.14) | `20` |
| `MpsPreMoveDaysBeforeProduction` | Days ahead for Task 1 | `1` |
| `NightlyMaintenanceRunHour` | Nightly run hour | `1` |
| `ThemePrimaryColor` / `ThemeSecondaryColor` | Brand colours (§5.17) | `#0a5e73` / `#fdb913` |
| `ThemeMode` | Site default mode | `system` |

Background services stay idle while their path settings are empty.

### 6.3 Seed data required before go-live

Entered through the Admin screens unless noted:

| Data | Notes |
|---|---|
| Stations | Every line, with component type, PRD folder (`UncPath`), archive folder, daily EqA target, display order |
| Job types | One per component type, with confirmed minutes per EqA (drives scheduling durations) |
| Shifts and breaks | At least one active shift with real break windows |
| Settings | Working hours, days, all four saw-file paths, at-risk buffer, creep threshold |
| SCH import paths | At least one |
| Batchcut watch paths | The for-cutting folder and the DONE archive base |
| Users | Real Admin account(s) — the dev `admin` account must not exist in production (§9.5) |
| Setup times / precedences | Optional; no screen yet, entered directly in the database |

---

## 7. Build Order

All eight steps are **complete**. Kept as a record, with notes where the result differs from the original plan.

| Step | Status | Notes |
|---|---|---|
| 1 — Database & EF Core | Done | Three migrations (§4.1) |
| 2 — Authentication | Done | Plus case-sensitive usernames and refresh-surviving sessions (§5.19) |
| 3 — Constraint resolution | Done | `OperatorAvail` built but not wired |
| 4 — Scheduling engine | Done | EDD in use; ATCS and TardinessCalc built but not wired |
| 5 — API | Done | 15 controllers (§3.4); OpenAPI instead of Swagger UI |
| 6 — Background services | Done | Five services; SCH parser corrected against real files |
| 7 — React front end | Done | Plus the Oct 2026 UI/UX round |
| 8 — Integration & validation | Done | Scenarios A–F automated (below) |

### Step 8 — Integration scenarios (automated)

Run end-to-end against a real SQL Server test database through the real API, with the background services driven one poll at a time.

| Scenario | Covers | Test file |
|---|---|---|
| A — Full lifecycle | Quote → finaldesign → bundle → approve → DONE → PRD → completed → nightly cleanup | `ScenarioA_FullJobLifecycleTests` |
| B — Scheduling correctness | EDD order, tardiness vs random, pinned jobs not moved | `ScenarioB_SchedulingCorrectnessTests` |
| C — Creep | Flag at threshold, frozen until approved, re-planned after | `ScenarioC_CreepDetectionTests` |
| D — Roles | Every mutating endpoint 403 for Viewer (routes discovered automatically), 401 anonymous | `ScenarioD_RoleEnforcementTests` |
| E — Nightly tasks | All five tasks and their log entries | `ScenarioE_NightlyTasksTests` |
| F — Capacity override | Override replaces the daily target | `ScenarioF_CapacityOverrideTests` |

Also: `AuthLoginTests` (username case), `PlanningFidelityTests`, `SiteTimeAndInputHardeningTests`, and parser tests that read the three real SCH files in `real_data/`.

**Outstanding sign-off from v1.0:** the operations team still needs to sign off Scenarios A and B on realistic data before a go-live date is set.

---

## 8. Design Decisions

### 8.1 Authentication *(current; to be replaced — see §9.2)*

- **Roles:** `Admin` (full access) and `Viewer` (read-only).
- **Accounts:** local, in the `Users` table, BCrypt-hashed, managed in Admin → Users.
- **Tokens:** JWT issued by the API, valid for 8 hours, kept in the browser's `sessionStorage` (§5.19).
- **API:** all `POST`/`PUT`/`DELETE` endpoints are Admin-only except logout, change-password and the quote estimate. `GET` endpoints need any valid token, except the Admin-only controllers (users, shifts, operators, admin paths). `401` for no token, `403` for the wrong role.
- **UI:** Viewers see every page except Settings, with all edit / approve / drag / Recalculate controls hidden.

### 8.2 "At Risk" definition *(re-activated, Oct 2026)*

- **Rule:** at risk when the scheduled completion falls between 0 and `AtRiskBufferMinutes` before the end of the due date, the job isn't completed and isn't already past due.
- **Late** (past due date) and **behind schedule** (past its own estimated completion) are separate flags (§5.0).
- **Computed on read** in `GET /api/jobs` — never stored. The only status written back on app load is `completed`.

### 8.3 Quote Estimator scope

Permanently time-based: queue tail + `MinsPerUnit × Qty`, respecting working hours, days and breaks. Setup times and operators are never factored in.

### 8.4 Export format

CSV only. `GET /api/schedule/export` with optional `stationId`, `dateFrom`, `dateTo`, combinable. Reports also export their visible table as CSV from the browser.

### 8.5 Manual overrides *(revised, Oct 2026)*

- **Drag-and-drop is now allowed** on the Calendar for Admins (same component type only).
- **Schedule manually** (popup, Dashboard Reschedule) pins a job to a station and date.
- **Edit modal** changes job details; any save re-runs the scheduler and returns the job plus the schedule result.
- **Recalculate** re-runs the scheduler on demand.

### 8.6 Shift structure

- One shift per day in practice; the schema supports more.
- Breaks are configurable per shift; breaks from all active shifts are treated as non-working time.
- The working window itself comes from the `ShiftStartHour` / `ShiftEndHour` settings, not from shift times.

### 8.7 Data retention

- 6 months (`ScheduleRetentionMonths`).
- Flagged `IsArchived` once older than that: completed jobs (by last update), schedule items (by generation time), batchcut events and maintenance logs. Unfinished jobs are never archived. Nothing is hard-deleted.
- Standard queries hide archived rows. There's no admin screen for archived records yet.

---

## 9. Current status, open items and planned work *(new)*

### 9.1 Recently completed (October 2026)

- Appearance settings: brand colours and site-wide light/dark/system default.
- Site-wide dark mode, with a per-user Auto / Light / Dark toggle in the top bar.
- Dashboard: at-risk and behind-schedule tiles, urgent list with Why badges and Approve / Reschedule / View actions.
- Calendar: Day / Week / Month, drag-and-drop, Recalculate, live now line.
- Login: Enter to submit, press-and-hold password reveal, session survives refresh, case-sensitive usernames.

### 9.2 Planned — Office 365 sign-in and new roles *(decided; do at the end, before deployment)*

- Replace the username/password login with **Microsoft Entra ID (Office 365) single sign-on**.
- The app defines **app roles: Admin, Supervisor, Kiosk**, replacing Admin / Viewer. IT assigns users **and groups** to these roles in Entra; the role arrives in the sign-in token and the API reads it, so role checks stay in the app but membership is managed by IT.
- Draft split, to be confirmed before building:

  | Role | Can do |
  |---|---|
  | Admin | Everything, including Settings and users |
  | Supervisor | Day-to-day scheduling: approve for cutting, drag/reschedule, Recalculate, approve creep, edit jobs. No Settings |
  | Kiosk | Read-only shop-floor display (Dashboard / Calendar); likely a shared Microsoft account per floor PC |

- The local `Users` table stays (without passwords), filled automatically on first sign-in, because approvals record who did them. Keep **one local break-glass admin** for when Microsoft sign-in is unavailable.
- Role management moves out of Admin → Users into Entra.
- Becomes unnecessary: password change/reset, password policy, the login form improvements.
- **Ask IT early** for the Entra app registration and an HTTPS address — both depend on other people.

### 9.3 Planned — Factories and kiosk display *(discussed; not started)*

- **Factories** defined in Admin; each **station** is assigned to a factory. Assigning stations (not product types) handles the same product being made in more than one factory.
- A full-screen **kiosk page** per factory (e.g. `/kiosk/factory-2`) for a shop-floor TV: no navigation, large text, auto-refresh, now line, keeps the screen awake, stays signed in for days. Can be built before the Office 365 switch.
- Optional: factory filter on the Calendar and Dashboard.
- **Open question — waiting on the workflow review:** should a job go to any factory with a matching line (today's behaviour), stay within one factory, or prefer a home factory with overflow?

### 9.4 Known gaps and issues

| # | Item | Impact |
|---|---|---|
| 1 | **Calendar after Recalculate with in-flight jobs** — cut / in-production jobs keep their original plan block, but the queue behind them re-flows from their *projected* end (§3.3). When the floor drifts from the plan, the Calendar shows overlapping blocks or gaps on that station | Misleading picture until the job finishes. Possible fix: keep the original plan for history but draw in-flight jobs at their projected position |
| 2 | Operators / skills not assigned to work (`OperatorAvail` not wired; `OperatorId` always empty; no Operators screen) | Operator availability isn't a scheduling constraint |
| 3 | No screens for Setup Times or Precedences; precedence only within one station | Database-only configuration |
| 4 | ATCS sequencing and the TardinessCalc local-search loop are built but not wired | Scheduling is EDD only |
| 5 | Quote stage doesn't compare the earliest viable production date with the Est. Booked Delivery Date | |
| 6 | Approve for cutting shows on every `ready` job, not only those whose bundle folder exists | Clicking returns a clear error |
| 7 | Dashboard station cards show current/next **job**, not current/next **member** | |
| 8 | Shift start/end times don't shape the working window (the Settings hours do) | Two places that look like they control hours |
| 9 | PRD reader untested against a real PaperlessJig file | Highest-value validation still outstanding |
| 10 | `MpsSourcePath` assumed to be `RawFilesPath` | Confirm the real MPS location |
| 11 | "Jobs late" tile means behind schedule; the sidebar "N late" means past due | Same word, two meanings |
| 12 | Calendar blocks show LATE but no at-risk badge; Calendar data doesn't auto-refresh | |
| 13 | No admin view of archived records | |
| 14 | Manual job creation is API-only (no UI button) | By design for now |
| 15 | "Jobs Completed vs Late" report counts currently-overdue jobs, not jobs that finished after their due date | Misses late completions once they're done |
| 16 | Setup times only apply between same-type jobs, because each station runs one component type | Changeover modelling is effectively unused |

### 9.5 Deployment checklist (not started)

- Host the API as a Windows service (or IIS) with **HTTPS**.
- Production database on SQL Server; apply migrations as a deliberate step (automatic only in Development).
- **Replace the JWT signing key** (a development key is committed in `appsettings.json`) — or remove it with the Office 365 switch.
- **Remove the dev `admin` / `Admin123!` account**; it is only created in Development, but no dev database should be promoted.
- Set `Site:TimeZoneId` if the server's time zone differs from the factory's.
- Fill in the go-live seed data (§6.3) and confirm real UNC paths and permissions for the service account.
- Build the front end (`npm run build`) and serve it from the API or a web server.
- Complete §9.2 (Office 365) before or during deployment.

---

## 10. Development and testing *(new)*

### 10.1 Running locally

| Server | Command | Address |
|---|---|---|
| API | `dotnet run --project src/ProductionScheduler.Api --launch-profile http` | http://localhost:5138 |
| Front end | `npm --prefix client run dev` | http://localhost:5173 (proxies `/api` to 5138) |

Both are also defined in `.claude/launch.json`. In Development the API applies migrations, seeds the settings and a default shift, and creates `admin` / `Admin123!` if no users exist.

**Fresh database:** stop the API, run `scripts/reset-dev-database.ps1 -Yes`, start the API again. Sample jobs are not part of the repo; they have been loaded with a one-off script (4 stations, 3 job types, ~40 jobs across all statuses, run through the real scheduler).

### 10.2 Tests

- `dotnet test` from the repo root: **136 tests** — engine and constraint unit tests, background-service tests (including the real SCH files), and the integration scenarios in §7.
- Integration tests use the separate `ProductionSchedulerIntegration` database with background services disabled and driven explicitly.
- Front-end changes are additionally checked in a real browser (Playwright scripts) against the running dev servers before being called done.
- Working practice: the project owner commits; fixes are verified live and confirmed before each commit.
