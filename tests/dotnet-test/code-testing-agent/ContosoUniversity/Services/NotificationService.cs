using System;
using System.Collections.Concurrent;
using ContosoUniversity.Models;

namespace ContosoUniversity.Services
{
    public class NotificationService : IDisposable
    {
        // Static shared queue across application instances (simple in-memory alternative to MSMQ)
        private static readonly ConcurrentQueue<Notification> _queue = new();
        private static readonly int _maxQueueLength = 500; // basic cap to prevent unbounded growth

        public NotificationService()
        {
            // No initialization required for in-memory queue
        }

        public void SendNotification(string entityType, string entityId, EntityOperation operation, string userName = null)
        {
            SendNotification(entityType, entityId, null, operation, userName);
        }

        public void SendNotification(string entityType, string entityId, string entityDisplayName, EntityOperation operation, string userName = null)
        {
            try
            {
                var notification = new Notification
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Operation = operation.ToString(),
                    Message = GenerateMessage(entityType, entityId, entityDisplayName, operation),
                    CreatedAt = DateTime.Now,
                    CreatedBy = userName ?? "System",
                    IsRead = false
                };

                _queue.Enqueue(notification);

                // Enforce max queue length (best-effort)
                while (_queue.Count > _maxQueueLength && _queue.TryDequeue(out _)) { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to enqueue notification: {ex.Message}");
            }
        }

        public Notification ReceiveNotification()
        {
            try
            {
                return _queue.TryDequeue(out var notification) ? notification : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to receive notification: {ex.Message}");
                return null;
            }
        }

        public void MarkAsRead(int notificationId)
        {
            // No-op for in-memory implementation (persistence not implemented)
        }

        private string GenerateMessage(string entityType, string entityId, string entityDisplayName, EntityOperation operation)
        {
            var displayText = !string.IsNullOrWhiteSpace(entityDisplayName)
                ? $"{entityType} '{entityDisplayName}'"
                : $"{entityType} (ID: {entityId})";

            return operation switch
            {
                EntityOperation.CREATE => $"New {displayText} has been created",
                EntityOperation.UPDATE => $"{displayText} has been updated",
                EntityOperation.DELETE => $"{displayText} has been deleted",
                _ => $"{displayText} operation: {operation}"
            };
        }

        public void Dispose()
        {
            // Nothing to dispose for in-memory queue
        }
    }
}
