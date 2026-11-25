# KVK company lookup, Netherlands (REST API enricher V2)

`kvk-process-script.js` is a V2 Process Script that looks up an organization's KVK number in the
Dutch Business Register by using the KVK Basisprofiel API and adds `kvk.*` properties to the
golden record. It needs a KVK API key.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › KVK (Netherlands)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/kvk-netherlands).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/KvkProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
