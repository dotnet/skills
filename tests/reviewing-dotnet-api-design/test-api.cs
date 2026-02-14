using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Contoso.Messaging;

// A message broker library for pub/sub messaging.
// Target: .NET 8, NuGet package.

public struct MessageEnvelope
{
    public string Topic { get; set; }
    public string CorrelationId { get; set; }
    public Dictionary<string, string> Headers { get; set; }
    public ReadOnlyMemory<byte> Payload { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

[Flags]
public enum DeliveryMode
{
    AtMostOnce = 1,
    AtLeastOnce = 2,
    ExactlyOnce = 3
}

public interface IMessageBroker
{
    void Publish(MessageEnvelope envelope);
    void Subscribe(string topic, Action<MessageEnvelope> handler);
    void Unsubscribe(string topic);
}

public class MessageBroker : IMessageBroker
{
    private readonly Dictionary<string, List<Action<MessageEnvelope>>> _handlers = new();

    public string BrokerEndpoint { get; set; }

    public List<string> ActiveTopics
    {
        get
        {
            var topics = new List<string>();
            foreach (var kvp in _handlers)
                if (kvp.Value.Count > 0)
                    topics.Add(kvp.Key);
            return topics;
        }
    }

    public void Publish(MessageEnvelope envelope)
    {
        if (envelope.Topic == null)
            throw new ArgumentException("Topic is required");

        if (_handlers.TryGetValue(envelope.Topic, out var handlers))
            foreach (var handler in handlers)
                handler(envelope);
    }

    public void Subscribe(string topicName, Action<MessageEnvelope> callback)
    {
        if (topicName == null) throw new ArgumentNullException();
        if (callback == null) throw new ArgumentNullException();

        if (!_handlers.ContainsKey(topicName))
            _handlers[topicName] = new List<Action<MessageEnvelope>>();
        _handlers[topicName].Add(callback);
    }

    public void Unsubscribe(string topic)
    {
        _handlers.Remove(topic);
    }

    public async Task PublishAsync(MessageEnvelope envelope, CancellationToken token)
    {
        await Task.Run(() =>
        {
            if (envelope.Topic == null)
                throw new ArgumentException("Topic is required");

            Publish(envelope);
        }, token);
    }

    public MessageEnvelope? Retrieval(string topic)
    {
        return null;
    }

    public int MessageCount
    {
        get
        {
            int count = 0;
            foreach (var kvp in _handlers)
                count += kvp.Value.Count;
            return count;
        }
    }

    public static bool operator ==(MessageBroker left, MessageBroker right)
        => left?.BrokerEndpoint == right?.BrokerEndpoint;
}

public class BrokerException : Exception
{
    public BrokerException(string msg) : base(msg) { }
}

public class MessageResult
{
    public bool Success { get; set; }
    public string Error { get; set; }

    public static MessageResult Parse(string raw)
    {
        if (raw == null) throw new Exception("Input required");
        var parts = raw.Split('|');
        if (parts.Length != 2) throw new Exception("Bad format");
        return new MessageResult { Success = parts[0] == "OK", Error = parts[1] };
    }
}
