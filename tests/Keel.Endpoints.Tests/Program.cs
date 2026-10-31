using Keel.Endpoints.Tests;

Console.WriteLine("Keel.Endpoints tests\n");
EndpointTests.Register();
AuthorizationTests.Register();
FluentValidationTests.Register();
GroupTests.Register();
TelemetryTests.Register();
IdempotencyTests.Register();
McpTests.Register();
AgentsTests.Register();
VersioningTests.Register();
OpenApiTests.Register();
#if KEEL_OPENAPI
OpenApiDocumentTests.Register();
#endif
AnalyzerTests.Register();
GeneratorTests.Register();
CodeFixTests.Register();
ManifestTests.Register();
ToolTests.Register();
PublicApiTests.Register();
return await Harness.RunAsync();
