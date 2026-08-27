namespace App.Services;

public sealed class DeploymentLabeler
{
    public string GetLabel()
        => Environment.GetEnvironmentVariable("DEPLOYMENT_SLOT") ?? "production";
}
