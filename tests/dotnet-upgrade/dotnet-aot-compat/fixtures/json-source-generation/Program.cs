var json = MessageSerializer.Serialize(new Message("ready", 3));

if (!json.Contains("\"Text\":\"ready\"", StringComparison.Ordinal) ||
    !json.Contains("\"Priority\":3", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Serialization behavior changed.");
}

Console.WriteLine("PASS");
