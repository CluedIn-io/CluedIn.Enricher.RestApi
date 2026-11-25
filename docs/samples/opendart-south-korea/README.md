# OpenDART company lookup, South Korea (REST API enricher V2)

`opendart-process-script.js` is a V2 Process Script that looks up an organization's 8-digit DART
corporation code in OpenDART's company overview API and adds `dart.*` properties to the golden
record. It needs an OpenDART API key.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › OpenDART (South Korea)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/opendart-south-korea).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/OpendartProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
