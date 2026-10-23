namespace Keel.Endpoints.Tests;

internal static class TestValidation
{
    /// <summary>
    /// The one AddValidation() call of this project. The .NET 10 validation generator emits a file per call site
    /// with the same name, so a second call site fails the build (CS8785, "hintName must be unique").
    /// </summary>
    public static IServiceCollection AddTestValidation(this IServiceCollection services) => services.AddValidation();
}
