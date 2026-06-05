using System;
using Xunit;

namespace MyApp.Tests;

public sealed class DbFixture : IDisposable
{
    public string Connection { get; } = "memory";
    public void Dispose() { }
}

[CollectionDefinition("Db", DisableParallelization = true)]
public class DbCollection : ICollectionFixture<DbFixture> { }
