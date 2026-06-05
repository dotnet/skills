using System.Reflection;
using Xunit.Sdk;

namespace MyApp.Tests;

public class DatabaseSetupAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest)
    {
        // Initialize test database
        base.Before(methodUnderTest);
    }

    public override void After(MethodInfo methodUnderTest)
    {
        // Cleanup test database
        base.After(methodUnderTest);
    }
}
