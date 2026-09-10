# Migrating a Connector/Enricher to Multi-Version Targeting

This document tracks the migration of `CluedIn.Enricher.RestApi` from a single-version build to the
multi-version targeting pattern. Part of a larger effort that has already migrated
`CluedIn.Connector.Dataverse.V2`, `CluedIn.Enricher.GoogleMaps`, `CluedIn.Connector.AzureEventHubs`,
`CluedIn.Connector.AzureDataLake`, and nine other enrichers (Gleif, OpenCorporates, Permid, Brreg,
KnowledgeGraph, ClearBit, CompanyHouse, CVR, DuckDuckGo).

This repo was explicitly referenced in `CluedIn.Enricher.GoogleMaps`'s own migration doc as **not**
yet migrated at that time, despite being cited in the shared pipeline template's own comments as an
example calling convention. Its pre-migration state was audited fresh here, not assumed correct.

Branch: `feature/multi-version-targeting` (off `develop`).

---

## Overview

Target matrix: `4.7.0`, `4.8.0`, `5.0.0-beta.*` — net6.0 for the 4.x line, net10.0 for 5.0.
Verified independently for this repo's own feeds via a throwaway restore: `5.0.0-*` resolves to
`5.0.0-beta.576`.

4.6.0 excluded — no evidence this repo needs it (no `IStreamRepository`/stream-API usage; this is a
generic REST-calling enricher, small API surface).

---

## Step 1 — Pipeline template (`azure-pipelines.yml`)

Status: **Done**

Replaced `crawler.build.yml` steps-template (plus its explicit `UseDotNet@2` installing 8.0 SDK —
stale, `global.json` already pins the real SDK) with `crawler.build.jobs.yml`. Also dropped
`createIntegrationEnvironmentScriptFilePath`/`Arguments` pointing at `./build/integration-test.ps1`
— that script doesn't exist in this repo (only `build/assets/`), same dead reference GoogleMaps'
migration found and removed in its own repo.

---

## Step 2 — `Directory.Build.props`

Status: **Done**

Honours `CluedInMultiVersionTargetFramework` (net10.0 local fallback), derives
`CLUEDIN_V47`/`V48`/`V50` `DefineConstants`, pins `LangVersion` to 13.0 up front.

---

## Step 3 — `Packages.props`

Status: **Done**

Renamed from lowercase `packages.props`. Guarded `_CluedIn`. Split test-tooling versions
conditionally on `CLUEDIN_V50` (xunit v2 2.9.3/AutoFixture.Xunit2 4.18.0/Microsoft.NET.Test.Sdk
17.12.0 below CluedIn 5.0, vs. the existing xunit.v3/AutoFixture.Xunit3/Microsoft.NET.Test.Sdk 18.3.0
above it — the repo already had the 5.0-side pinned, just not the pre-5.0 side).

`CluedIn.Testing.Base`/`CluedIn.CrawlerIntegrationTesting` switched to the version-suffixed package
IDs (`.470`/`.480`/`.500`, computed as `_CluedInVersionOnly.Replace('.', '')`) per the GoogleMaps
precedent — confirmed directly against the feed that `.470`/`.480`/`.500` all exist, each currently
at `1.0.0-beta.1`.

---

## Step 4 — `NuGet.config`

Status: **Done**

Renamed from `Nuget.config`. No `public` feed needed — confirmed via throwaway restore that
`CluedIn.ExternalSearch` at `4.7.0` restores cleanly against the repo's existing feeds
(`nuget.org`/`develop`/`release`/`AzurePipelines`).

---

## Step 5 — Test project

Status: **Done**

`test/Directory.Build.props` stripped to just `IsTestProject` (was unconditionally pulling in
`xunit.v3`/`AutoFixture.Xunit3` — the CS0433 trap the MasterDataServices doc describes). Conditional
`ItemGroup`s (xunit v2/v3 split) added directly to
`test/integration/Integration.Tests/ExternalSearch.RestApi.Integration.Tests.csproj`. The repo's
only test (`Dummy.cs`, a bare `[Fact]`) doesn't use AutoFixture or `ITestOutputHelper`, so no
`GlobalUsings.cs` was needed — kept the conditional AutoFixture references anyway for parity with
every other migrated repo, in case future tests need them.

Verified with real `dotnet test` runs (not just build) on both the 4.7.0/net6.0 (xunit v2) and
5.0.0-beta.*/net10.0 (xunit v3) legs — 1/1 passing on each.

---

## Step 6 — API compatibility audit

Status: **Done**

Unlike most enrichers in this effort, `RestApiExternalSearchProvider.cs` does directly use
`RestSharp` (`RestClient`, `RestRequest`) alongside an unrelated `HttpClient`-based path
(`Models/ScriptHttpClient.cs`, used for user-scripted requests via Jint — no RestSharp there, no
break). One real break found: `GetHttpMethod(string)` returned `Method.Get`/`Method.Post`
(RestSharp 114.x PascalCase) which doesn't exist on RestSharp 106.x (needs `Method.GET`/`Method.POST`
uppercase). Fixed with `#if CLUEDIN_V50` in the method body. The `client.ExecuteAsync(...).Result`
call site uses a `var`-inferred local, so it needed no guard (same finding as GoogleMaps' doc); the
`.Headers.Select(x => new HeaderDto { ... Value = x.Value?.ToString() })` call already normalizes
with `.ToString()`, so it isn't affected by `Parameter.Value` being `object` vs `string` across
RestSharp generations (the break CompanyHouse hit) — no change needed there.

Verified: both src projects and the integration test project build and test clean (0 errors, real
`dotnet test` passing) against all three targets.

---

## Step 7 — Reset the semantic version (`GitVersion.yml`)

Status: **Done**

This repo's `GitVersion.yml` already had an `ignore:` block (`ignore: sha: []`) — merged
`commits-before` into it rather than adding a second top-level `ignore:` key, which would silently
win-by-duplicate-key and clobber the existing block with zero YAML error (a trap CompanyHouse and
CVR both hit in this same effort).

```yaml
next-version: 1.0
ignore:
  sha: []
  commits-before: 2026-08-13T00:00:00
```

Highest pre-existing tag is `4.6.3` at `2026-08-10T11:22:51+08:00`. Padded ~2.5 days past it
(the 2-day minimum this effort settled on after `GitVersion.Tool 5.9.0` was found to parse
`commits-before` using local machine time, not UTC, and fail silently on a too-tight margin).
Verified directly: `MajorMinorPatch: "1.0.0"`, `SemVer: "1.0.0-multi-version-targeting.80"`.

---

## Checklist

- [x] `azure-pipelines.yml` — switched to `crawler.build.jobs.yml` with `multiVersionCluedInTargets` (4.7.0, 4.8.0, 5.0.0-beta.*); dead `integration-test.ps1` reference removed
- [x] `Directory.Build.props` — honours `CluedInMultiVersionTargetFramework`; `DefineConstants` derived; `LangVersion` pinned to 13.0
- [x] `Packages.props` — renamed from lowercase; `_CluedIn` guarded; test-tooling versions split by `CLUEDIN_V50`; `CluedIn.Testing.Base`/`CluedIn.CrawlerIntegrationTesting` switched to suffixed package IDs
- [x] `NuGet.config` — renamed from `Nuget.config`; no extra feed needed
- [x] Test project — `test/Directory.Build.props` stripped to `IsTestProject`; conditional xunit v2/v3 `ItemGroup`s added to the integration test csproj; verified with real `dotnet test` on both xunit generations
- [x] Source — one RestSharp 106-vs-114 break fixed (`GetHttpMethod`, `#if CLUEDIN_V50`)
- [x] `GitVersion.yml` — merged into the pre-existing `ignore:` block (not a second one); `commits-before` padded ~2.5 days past the highest tag; verified `MajorMinorPatch: 1.0.0`
- [ ] Push branch and confirm the actual Azure DevOps pipeline run is green end-to-end
