# APR Companies Open Data company lookup, Serbia (REST API enricher V2)

`apr-process-script.js` is a V2 Process Script that looks up an organization's Serbian registration
number (matični broj) in the APR Companies Open Data API and adds `apr.*` properties to the golden
record.

The settings, the properties it adds, a sample request and response, how it handles errors, and its
limitations are documented in one place, the user documentation: [REST API enricher samples › APR
(Serbia)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/apr-serbia).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/AprProcessScriptTests.cs` runs it with the same script
engine, limits and result handling as the enricher, so a change to the enricher that breaks the
script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
