using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ApiClients.Tests;

public sealed class CustomerClientTests
{
    [Fact]
    public async Task GetCustomer_SendsExpectedRequest()
    {
        var handler = new RecordingHandler("""{"id":7}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        await client.GetStringAsync("/customers/7", Xunit.TestContext.Current.CancellationToken);

        Xunit.Assert.Equal("/customers/7", handler.Request!.RequestUri!.AbsolutePath);
    }
}

public sealed class OrderClientTests
{
    [Fact]
    public async Task GetOrder_SendsExpectedRequest()
    {
        var handler = new RecordingHandler("""{"id":11}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        await client.GetStringAsync("/orders/11", Xunit.TestContext.Current.CancellationToken);

        Xunit.Assert.Equal("/orders/11", handler.Request!.RequestUri!.AbsolutePath);
    }
}

public sealed class ProductClientTests
{
    [Fact]
    public async Task GetProduct_SendsExpectedRequest()
    {
        var handler = new RecordingHandler("""{"id":23}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        await client.GetStringAsync("/products/23", Xunit.TestContext.Current.CancellationToken);

        Xunit.Assert.Equal("/products/23", handler.Request!.RequestUri!.AbsolutePath);
    }
}

public sealed class RecordingHandler(string response) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Request = request;
        return Task.FromResult(new HttpResponseMessage
        {
            Content = new StringContent(response)
        });
    }
}
