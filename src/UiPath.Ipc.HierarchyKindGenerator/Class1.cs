using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace UiPath.Ipc.HierarchyKindGenerator;

[Generator]
public class HelloWorldGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization required for this example
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var symbolofRecordBase = context.Compilation.GetTypeByMetadataName("UiPath.Ipc.Telemetry+RecordBase");
        var symbolofTelemetry = context.Compilation.GetTypeByMetadataName("UiPath.Ipc.Telemetry");
        var leaves = symbolofRecordBase.GetSubtypesNestedBy(symbolofTelemetry).Where(x => !x.IsAbstract).ToArray();
        
        // Create the source code to be generated
        var source = $$"""
            using System;
            
            namespace UiPath.Ipc
            {
                partial class Telemetry
                {
                    public enum RecordKind
                    {
            {{string.Join("\r\n", leaves.Select(x => $"            {x.Name},"))}}
                    }
                
                    partial record RecordBase
                    {
                        public RecordKind Kind => (RecordKind)Enum.Parse(typeof(RecordKind), GetType().Name);
                    }
                }
            }
            """;

        context.AddSource("RecordKind.cs", SourceText.From(source, Encoding.UTF8));
    }
}

internal static class Extensions
{
    public static IEnumerable<INamedTypeSymbol> GetSubtypesNestedBy(this INamedTypeSymbol baseTypeSymbol, INamedTypeSymbol nesterTypeSymbol)
    {
        var allNestedTypes = nesterTypeSymbol.GetNestedTypes();
        var subtypes = new List<INamedTypeSymbol>();

        foreach (var type in allNestedTypes)
        {
            if (IsSubtypeOf(type, baseTypeSymbol))
            {
                subtypes.Add(type);
            }
        }

        return subtypes;
    }

    public static bool IsSubtypeOf(this INamedTypeSymbol type, INamedTypeSymbol baseTypeSymbol)
    {
        var current = type.BaseType;

        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseTypeSymbol))
            {
                return true;
            }
            current = current.BaseType;
        }

        return false;
    }

    public static IEnumerable<INamedTypeSymbol> GetNamespaceTypes(this INamespaceSymbol namespaceSymbol)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (var type in ns.GetNamespaceTypes())
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
            }
        }
    }

    public static IEnumerable<INamedTypeSymbol> GetNestedTypes(this INamedTypeSymbol typeSymbol)
    {
        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            yield return nestedType;

            // Recursively get nested types of the nested type
            foreach (var nestedNestedType in GetNestedTypes(nestedType))
            {
                yield return nestedNestedType;
            }
        }
    }
}