# Playtest checklist — 3a (rules library)

1. Run `dotnet test rules/RadiantPool.Rules.sln` → expect **72 passed, 0 failed**.
2. Inspect `rules/RadiantPool.Rules.Tests/TurnEngineTests.cs :: FullSkirmish_TwoPcsVsTwoSkulkers_Deterministic` — a seeded 2 PC vs 2 monster fight runs to completion through initiative, action economy, attacks, cantrips, death saves.
3. Confirm zero Unity references: `rules/RadiantPool.Rules/RadiantPool.Rules.csproj` targets netstandard2.1 with no package dependencies.
