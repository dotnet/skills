namespace MessagingApi.Dtos;

public record NamespaceResponse(string Name, string Location, string Sku, IReadOnlyDictionary<string, string> Tags);

public record CreateNamespaceRequest(string Name, string Location, string Sku, Dictionary<string, string>? Tags);

public record UpdateNamespaceRequest(string Location, string Sku, Dictionary<string, string>? Tags);
