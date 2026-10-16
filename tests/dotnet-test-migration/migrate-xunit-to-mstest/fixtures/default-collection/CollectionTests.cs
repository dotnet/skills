using System;
using Xunit;

[CollectionDefinition("Shared")]
public sealed class SharedCollection : ICollectionFixture<SharedResource> { }

public sealed class SharedResource : IDisposable
{
    internal LifecycleProbe Probe { get; } = new();
    public void Dispose() => Probe.Dispose();
}

[Collection("Shared")]
public class FirstMember
{
    private readonly SharedResource resource;
    public FirstMember(SharedResource resource) => this.resource = resource;

    [Fact]
    public void FirstCase()
    {
        resource.Probe.Enter(nameof(FirstCase));
        Assert.Equal(5, 2 + 3);
        resource.Probe.Exit(nameof(FirstCase));
    }
}

[Collection("Shared")]
public class SecondMember
{
    private readonly SharedResource resource;
    public SecondMember(SharedResource resource) => this.resource = resource;

    [Fact]
    public void SecondCase()
    {
        resource.Probe.Enter(nameof(SecondCase));
        Assert.Equal(1, -2 + 3);
        resource.Probe.Exit(nameof(SecondCase));
    }
}
