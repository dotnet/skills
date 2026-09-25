using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Contoso.Sync.Services;

public sealed class InventorySyncService
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("https://inventory.contoso.example/"),
    };

    public async Task<int> GetOnHandQuantityAsync(string sku)
    {
        var response = await Client.GetAsync($"v1/stock/{sku}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);

        return document.RootElement.GetProperty("onHand").GetInt32();
    }

    public async Task<bool> ReserveAsync(string sku, int quantity)
    {
        var body = new StringContent($"{{\"sku\":\"{sku}\",\"quantity\":{quantity}}}");
        var response = await Client.PostAsync("v1/reservations", body);

        return response.IsSuccessStatusCode;
    }
}
