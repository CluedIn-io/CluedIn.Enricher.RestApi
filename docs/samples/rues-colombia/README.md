# RUES company lookup, Colombia (REST API enricher V2)

`rues-process-script.js` is a V2 Process Script that looks up an organization's Colombian NIT in
the Registro Único Empresarial y Social (RUES), using the extract of the commercial register that
Confecámaras publishes on datos.gov.co, and adds `rues.*` properties to the golden record.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › RUES (Colombia)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/rues-colombia).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/RuesProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
