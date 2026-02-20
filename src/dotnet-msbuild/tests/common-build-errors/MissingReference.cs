using System;

namespace MissingReference;

public class DataProcessor
{
    public string Serialize(object data)
    {
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    public void Process(Microsoft.Extensions.Logging.ILogger logger)
    {
        logger.LogInformation("Processing");
    }
}
