using System.Threading.Tasks;
using Xunit;

[CollectionDefinition("Shared")]
public sealed class SharedCollection : ICollectionFixture<AsyncSharedResource> { }

public sealed class AsyncSharedResource : IAsyncLifetime
{
    internal AsyncLifecycleProbe Probe { get; } = new();
    public Task InitializeAsync() => Probe.InitializeAsync();
    public Task DisposeAsync() => Probe.DisposeAsync();
}

[Collection("Shared")]
public class FirstMember
{
    private readonly AsyncSharedResource resource;
    public FirstMember(AsyncSharedResource resource) => this.resource = resource;

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
    private readonly AsyncSharedResource resource;
    public SecondMember(AsyncSharedResource resource) => this.resource = resource;

    [Fact]
    public void SecondCase()
    {
        resource.Probe.Enter(nameof(SecondCase));
        Assert.Equal(1, -2 + 3);
        resource.Probe.Exit(nameof(SecondCase));
    }
}
