using Newtonsoft.Json;

var payload = JsonConvert.SerializeObject(new { sku = "A100", inStock = true });
Console.WriteLine(payload);
