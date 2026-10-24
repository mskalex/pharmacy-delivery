---
name: format-code
description: Format and inspect PharmacyDelivery C# code with the ReSharper command line tools (cleanup profile "PharmacyDelivery: Full Cleanup", inspectcode) and fix what the build then reports. Use before finishing a change or when asked to format code.
---

# Format code (ReSharper CLT)

1. `dotnet tool restore` (installs `jetbrains.resharper.globaltools` from `.config/dotnet-tools.json`).
2. Cleanup:
   - product: `dotnet jb cleanupcode src/PharmacyDelivery.slnx --profile="PharmacyDelivery: Full Cleanup" --exclude="**/Migrations/**" --verbosity=WARN`
   - tests: `dotnet jb cleanupcode src/PharmacyDelivery.Tests.slnx --profile="PharmacyDelivery: Full Cleanup" --include="**/PharmacyDelivery.Tests/**/*.cs" --verbosity=WARN`
   - limit to changed files with `--include="<path>;<path>"` when the change is small.
   The profile in `PharmacyDelivery.Tests.slnx.DotSettings` does not optimize usings: the test solution contains only the test project, so
   ReSharper sees the product projects as compiled assemblies and would remove usings it cannot resolve (e.g. EF Core). Unused usings are
   still an error in the build (IDE0005), so nothing is lost.
   If the profile is "not found", the `.DotSettings` file is invalid for ReSharper (e.g. contains XML comments) — fix the file, do not
   switch to a built-in profile.
3. Rebuild both solutions. Cleanup can wrap a long one-line `if`/`foreach` body onto two lines; the project rule then requires braces
   (`IDE0011`) — add them, or shorten the statement so it fits on one line (max 140 characters).
4. Optional inspection: `dotnet jb inspectcode src/PharmacyDelivery.slnx --severity=WARNING --output=inspect.sarif --format=Sarif` and fix findings in
   changed files.
5. Do not reformat generated code (`Migrations/`). Report which files changed.
