using Keel.Endpoints.Tests;

Console.WriteLine("Keel.Endpoints tests\n");
EndpointTests.Register();
AuthorizationTests.Register();
FluentValidationTests.Register();
OpenApiTests.Register();
#if KEEL_OPENAPI
OpenApiDocumentTests.Register();
#endif
AnalyzerTests.Register();
return await Harness.RunAsync();
