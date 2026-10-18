using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Keel.Endpoints.Internal;

/// <summary>
/// Finds and applies <see cref="EndpointGroup"/> classes. The generated registration finds groups at
/// compile time and passes them in; the reflective registration discovers them here.
/// </summary>
internal static class EndpointGroups
{
    private static readonly ConcurrentDictionary<Assembly, Type[]> GroupTypes = new();

    /// <summary>The groups covering <paramref name="endpointType"/>, outermost namespace first.</summary>
    [RequiresUnreferencedCode(EndpointDescriptor.ReflectionMessage)]
    public static IReadOnlyList<EndpointGroup> For(Type endpointType)
    {
        var groupTypes = GroupTypes.GetOrAdd(endpointType.Assembly, Discover);
        var endpointNamespace = endpointType.Namespace ?? string.Empty;

        return groupTypes
            .Where(g => Covers(g.Namespace ?? string.Empty, endpointNamespace))
            .Select(Create)
            .ToArray();
    }

    /// <summary>Applies each group's configuration to the endpoint, in order.</summary>
    public static void Apply(RouteHandlerBuilder builder, Type endpointType, IReadOnlyList<EndpointGroup> groups)
    {
        foreach (var group in groups)
        {
            var options = new EndpointOptions();
            group.Configure(options);

            if (options.Binding is not null || options.StreamFormat != StreamFormat.JsonArray || options.EventType is not null)
            {
                throw new InvalidOperationException(
                    $"Endpoint group {group.GetType().FullName} sets Binding, StreamFormat or EventType, which belong to endpoints " +
                    $"(it covers {endpointType.FullName}). Set them on the contract ([BindRequest]), the class ([ServerSentEvents]) " +
                    "or in the endpoint's Configure.");
            }

            foreach (var convention in options.Conventions) convention(builder);
        }
    }

    /// <summary>A group in the global namespace covers everything; otherwise its namespace and below.</summary>
    internal static bool Covers(string groupNamespace, string endpointNamespace) =>
        groupNamespace.Length == 0
        || endpointNamespace == groupNamespace
        || endpointNamespace.StartsWith(groupNamespace + ".", StringComparison.Ordinal);

    [RequiresUnreferencedCode(EndpointDescriptor.ReflectionMessage)]
    private static Type[] Discover(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                        && typeof(EndpointGroup).IsAssignableFrom(t))
            // Outermost first: fewer namespace segments first, then by name for a stable order.
            .OrderBy(t => t.Namespace is { Length: > 0 } ns ? ns.Count(c => c == '.') + 1 : 0)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

    [RequiresUnreferencedCode(EndpointDescriptor.ReflectionMessage)]
    private static EndpointGroup Create(Type groupType)
    {
        if (groupType.GetConstructor(Type.EmptyTypes) is not { IsPublic: true })
        {
            throw new InvalidOperationException(
                $"Endpoint group {groupType.FullName} needs a public parameterless constructor. Groups configure endpoints; they do not take dependencies.");
        }

        return (EndpointGroup)Activator.CreateInstance(groupType)!;
    }
}
