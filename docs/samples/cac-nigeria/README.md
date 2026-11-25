# CAC (Nigeria) (REST API enricher V2)

`cac-process-script.js` is a V2 Process Script that looks up an organization's RC, BN or IT number
with the Corporate Affairs Commission's CAC-verified VAS API and adds `cac.*` properties to the
golden record. It needs a VAS API key, and each call is charged to the VAS account balance.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › CAC (Nigeria)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/cac-nigeria).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/CacProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
