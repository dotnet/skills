using System;
namespace MyApp.Tests;
public sealed class DbFixture : IDisposable
{
    public string ConnectionString { get; } = "data source=:memory:";
    public void Dispose() { }
}
