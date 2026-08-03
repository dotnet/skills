using System.ComponentModel.DataAnnotations;

namespace MessagingApi.Models;

// Owned child of a namespace; itself owns subscriptions.
public class Topic
{
    public Guid Id { get; set; }
    public Guid NamespaceId { get; set; }
    public MessagingNamespace? Namespace { get; set; }

    public required string Name { get; set; }
    public int MaxSizeInMegabytes { get; set; } = 1024;
    public EntityStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    [Timestamp] public byte[] RowVersion { get; set; } = [];

    public List<Subscription> Subscriptions { get; set; } = new();
}
