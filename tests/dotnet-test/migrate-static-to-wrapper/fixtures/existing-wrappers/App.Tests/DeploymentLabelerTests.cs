using App.Services;
using Xunit;

namespace App.Tests;

public sealed class DeploymentLabelerTests
{
    [Fact]
    public void GetLabel_uses_deployment_slot()
    {
        const string variable = "DEPLOYMENT_SLOT";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "staging");
            Assert.Equal("staging", new DeploymentLabeler().GetLabel());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
