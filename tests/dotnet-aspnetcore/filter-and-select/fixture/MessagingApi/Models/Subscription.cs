using System.ComponentModel.DataAnnotations;

namespace MessagingApi.Models;

// Owned grandchild: a subscription belongs to a topic.
public class Subscription
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public Topic? Topic { get; set; }

    public required string Name { get; set; }
    public int MaxDeliveryCount { get; set; } = 10;
    public EntityStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
