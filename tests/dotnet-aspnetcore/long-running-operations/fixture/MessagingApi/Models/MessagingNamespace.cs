using System.ComponentModel.DataAnnotations;

namespace MessagingApi.Models;

// Aggregate root: a messaging namespace owns its queues, topics, and authorization rules.
public class MessagingNamespace
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Location { get; set; }
    public NamespaceSku Sku { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public ProvisioningState ProvisioningState { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }

    // Soft delete.
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Concurrency token (drives the content ETag).
    [Timestamp] public byte[] RowVersion { get; set; } = [];

    public List<Queue> Queues { get; set; } = new();
    public List<Topic> Topics { get; set; } = new();
    public List<AuthorizationRule> AuthorizationRules { get; set; } = new();
}
