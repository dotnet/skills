// Stubs for Azure AutoRest codegen types (not available as a public NuGet package)
#nullable enable

// GeneratorPageableHelpers is a codegen-emitted copy of PageableHelpers
namespace Azure.Core
{
    internal static class GeneratorPageableHelpers
    {
        public static AsyncPageable<T> CreateAsyncPageable<T>(
            System.Func<int?, HttpMessage>? createFirstPageRequest,
            System.Func<int?, string, HttpMessage>? createNextPageRequest,
            System.Func<System.Text.Json.JsonElement, T> valueFactory,
            Pipeline.ClientDiagnostics clientDiagnostics,
            Pipeline.HttpPipeline pipeline,
            string scopeName,
            string? itemPropertyName,
            string? nextLinkPropertyName,
            System.Threading.CancellationToken cancellationToken) where T : notnull
            => PageableHelpers.CreateAsyncPageable(createFirstPageRequest, createNextPageRequest, valueFactory, clientDiagnostics, pipeline, scopeName, itemPropertyName, nextLinkPropertyName, cancellationToken);

        public static Pageable<T> CreatePageable<T>(
            System.Func<int?, HttpMessage>? createFirstPageRequest,
            System.Func<int?, string, HttpMessage>? createNextPageRequest,
            System.Func<System.Text.Json.JsonElement, T> valueFactory,
            Pipeline.ClientDiagnostics clientDiagnostics,
            Pipeline.HttpPipeline pipeline,
            string scopeName,
            string? itemPropertyName,
            string? nextLinkPropertyName,
            System.Threading.CancellationToken cancellationToken) where T : notnull
            => PageableHelpers.CreatePageable(createFirstPageRequest, createNextPageRequest, valueFactory, clientDiagnostics, pipeline, scopeName, itemPropertyName, nextLinkPropertyName, cancellationToken);
    }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    internal class CodeGenTypeAttribute : System.Attribute
    {
        public CodeGenTypeAttribute(string originalName) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
    internal class CodeGenSuppressAttribute : System.Attribute
    {
        public CodeGenSuppressAttribute(string member, params System.Type[] parameters) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
    internal class CodeGenSuppressTypeAttribute : System.Attribute
    {
        public CodeGenSuppressTypeAttribute(string typeName) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = true)]
    internal class CodeGenMemberAttribute : System.Attribute
    {
        public CodeGenMemberAttribute(string originalName) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
    internal class CodeGenSerializationAttribute : System.Attribute
    {
        public CodeGenSerializationAttribute(string propertyName) { }
        public string? SerializationValueHook { get; set; }
        public string? DeserializationValueHook { get; set; }
    }
}
