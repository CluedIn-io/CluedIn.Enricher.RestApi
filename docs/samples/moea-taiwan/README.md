# Ministry of Economic Affairs company lookup, Taiwan (REST API enricher V2)

`moea-process-script.js` is a V2 Process Script that looks up an organization's Taiwanese Unified
Business Number in the Ministry of Economic Affairs' company registration open data (GCIS) and adds
`moea.*` properties to the golden record.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › Ministry of Economic Affairs (Taiwan)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/moea-taiwan).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/MoeaProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
