# MCA company master data lookup, India (REST API enricher V2)

`mca-process-script.js` is a V2 Process Script that looks up an organization's Corporate
Identification Number (CIN) in the Ministry of Corporate Affairs company master data on data.gov.in
and adds `mca.*` properties to the golden record. It needs a data.gov.in API key.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › MCA company master data (India)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/mca-india).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/McaProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
