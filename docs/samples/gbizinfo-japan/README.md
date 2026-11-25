# gBizINFO company lookup, Japan (REST API enricher V2)

`gbizinfo-process-script.js` is a V2 Process Script that looks up an organization's 13-digit
Japanese corporate number in gBizINFO's REST API and adds `gbiz.*` properties to the golden record.
It needs a gBizINFO API token.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › gBizINFO (Japan)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/gbizinfo-japan).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/GbizinfoProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
