using TestProject.Generated;

Console.WriteLine("=== Build Information (from generated code) ===");
Console.WriteLine($"Project Name:     {BuildInfo.ProjectName}");
Console.WriteLine($"Build Time:       {BuildInfo.BuildTime}");
Console.WriteLine($"Machine Name:     {BuildInfo.MachineName}");
Console.WriteLine($"Configuration:    {BuildInfo.Configuration}");
Console.WriteLine($"Target Framework: {BuildInfo.TargetFramework}");
Console.WriteLine("================================================");
