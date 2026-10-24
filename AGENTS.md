# AGENTS.md

Instructions for coding agents live in [CLAUDE.md](CLAUDE.md) (project map, commands, rules) and [docs/ai/](docs/ai/) (architecture, domain,
conventions, concurrency, messaging, testing, runbook). They apply to every agent, not only Claude.

Minimum before finishing a change:

```bash
dotnet build src/PharmacyDelivery.slnx && dotnet build src/PharmacyDelivery.Tests.slnx
dotnet test src/PharmacyDelivery.Tests.slnx --filter "TestCategory!=Integration"
```

Run the full suite (`dotnet test src/PharmacyDelivery.Tests.slnx`, needs Docker) when the change touches the database, messaging, Redis or the API.
