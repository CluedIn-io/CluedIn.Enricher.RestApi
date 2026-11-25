# Malta Business Registry (REST API enricher V2)

`mbr-process-script.js` is a V2 Process Script that looks up an organization's Maltese company
registration number with the Malta Business Registry's Basic Company Details API package and adds
`mbr.*` properties to the golden record. It is a template: MBR gives subscribers the endpoint and
authentication details, which go in `MBR_URL` and `API_KEY_HEADER` at the top of the script.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › Malta Business Registry](https://documentation.cluedin.net/govern/rest-api-enricher-samples/mbr-malta).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/MbrProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
