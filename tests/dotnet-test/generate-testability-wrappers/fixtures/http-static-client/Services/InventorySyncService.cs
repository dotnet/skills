using System;
using System.Net.Http;
using System.Net.Http.Json;
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
        var encodedSku = Uri.EscapeDataString(sku);
        var response = await Client.GetAsync($"v1/stock/{encodedSku}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<StockResponse>();

        return payload?.OnHand
            ?? throw new InvalidOperationException("Inventory response did not contain onHand.");
    }

    public async Task<bool> ReserveAsync(string sku, int quantity)
    {
        var body = JsonContent.Create(new { sku, quantity });
        var response = await Client.PostAsync("v1/reservations", body);

        return response.IsSuccessStatusCode;
    }

    private sealed record StockResponse(int OnHand);
}
