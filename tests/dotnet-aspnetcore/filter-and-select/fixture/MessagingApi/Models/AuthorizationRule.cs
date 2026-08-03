using System.ComponentModel.DataAnnotations;

namespace MessagingApi.Models;

// Owned child of a namespace: an access policy with regenerable keys.
public class AuthorizationRule
{
    public Guid Id { get; set; }
    public Guid NamespaceId { get; set; }
    public MessagingNamespace? Namespace { get; set; }

    public required string Name { get; set; }
    public AccessRight Rights { get; set; }
    public string PrimaryKey { get; set; } = "";
    public string SecondaryKey { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }

    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
