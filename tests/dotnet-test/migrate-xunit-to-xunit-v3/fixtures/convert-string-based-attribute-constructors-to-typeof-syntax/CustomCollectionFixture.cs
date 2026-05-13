using Xunit;

[assembly: CollectionBehavior("MyApp.Tests.CustomCollectionFactory", "TestProject")]

namespace MyApp.Tests;

public class CustomCollectionFactory { }

public class DatabaseFixture : IDisposable
{
    public DatabaseFixture() { }
    public void Dispose() { }
}
