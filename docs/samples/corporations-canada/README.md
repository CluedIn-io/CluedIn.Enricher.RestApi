# Corporations Canada federal corporation lookup (REST API enricher V2)

`corporations-canada-process-script.js` is a V2 Process Script that looks up an organization's federal corporation number or business number
with the Corporations Canada Federal Corporation API and adds
`corporationscanada.*` properties to the golden record.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › Corporations Canada](https://documentation.cluedin.net/govern/rest-api-enricher-samples/corporations-canada).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/CorporationsCanadaProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
