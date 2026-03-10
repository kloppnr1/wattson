# Contributing to WattsOn

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) with Compose V2
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 22](https://nodejs.org/) (for frontend development)
- [Git](https://git-scm.com/)

## Local Development Setup

### 1. Clone and configure

```bash
git clone https://github.com/kloppnr1/wattson.git
cd wattson
```

Copy the example env file and fill in credentials:

```bash
cp .env.example .env   # adjust DB_PASSWORD and DataHub credentials
```

### 2. Start the stack

```bash
docker compose up -d
```

This starts the database (TimescaleDB), the API, the Worker, and the frontend. The API runs on port 8080, the frontend on port 5173 (dev) or 80 (docker).

### 3. Run the API locally (without Docker)

```bash
cd src/WattsOn.Api
dotnet run
```

Requires a running TimescaleDB instance (start just the db via `docker compose up db -d`).

### 4. Run the frontend locally

```bash
cd src/WattsOn.Frontend
npm install
npm run dev
```

The dev server proxies API calls to the backend.

## Running Tests

### Unit tests

```bash
dotnet test
```

Tests live in `tests/` and cover domain logic, application handlers, and infrastructure. No external dependencies required.

### Specific project

```bash
dotnet test tests/WattsOn.Domain.Tests
```

## Code Style

- Follow the existing patterns in each layer (domain, application, infrastructure, API).
- Domain services are pure static methods — no persistence or side effects.
- Handlers receive a `DbContext`, do the work, and return results for the caller to persist.
- No separate style config exists; match the surrounding code.

## Branch and PR Process

- Branch off `main` for all changes.
- Use descriptive branch names: `feat/brs-038-support`, `fix/supply-date-overlap`, `docs/contributing`.
- Open a pull request against `main`. CI must pass before merging.
- Keep PRs focused — one logical change per PR.

## Reporting Bugs

Open a GitHub issue with steps to reproduce, expected vs actual behaviour, and relevant log output.
