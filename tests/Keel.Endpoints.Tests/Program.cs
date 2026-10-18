using Keel.Endpoints.Tests;

Console.WriteLine("Keel.Endpoints tests\n");
EndpointTests.Register();
AuthorizationTests.Register();
FluentValidationTests.Register();
AnalyzerTests.Register();
return await Harness.RunAsync();
