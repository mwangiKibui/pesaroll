# Pesaroll

HR and payroll for East Africa, starting with Kenya: self-serve setup, correct statutory deductions (PAYE, SHIF, NSSF, Housing Levy, NITA), payslips and leave on a phone, for groups with several companies.

## Stack

- API and worker: C# on .NET 10 (ASP.NET Core, EF Core with Npgsql, Hangfire, QuestPDF)
- App: React with TypeScript (Vite)
- Database: PostgreSQL (DigitalOcean Managed), schema as code in `database/`, no migrations
- Hosting: DigitalOcean Droplets, Spaces, Container Registry

See `CLAUDE.md` for the layout, commands and the rules every change follows.

## Getting started

You need Docker and nothing else.

```bash
docker compose up -d --build                  # database, schema, API and web app, all running
                                              # app http://localhost:5173, API http://localhost:8080/swagger
docker compose down -v                        # stop and wipe the local database
scripts/check.sh                              # all checks, run before every pull request
```
