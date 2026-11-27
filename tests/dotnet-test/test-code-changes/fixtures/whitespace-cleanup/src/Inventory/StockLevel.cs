namespace Inventory;

public sealed class StockLevel
{
	private readonly Dictionary<string, int> _onHand = new(StringComparer.OrdinalIgnoreCase);

	public int OnHand(string sku) => _onHand.TryGetValue(sku, out var quantity) ? quantity : 0;   

	public void Receive(string sku, int quantity)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
		_onHand[sku] = OnHand(sku)+quantity;
	}

	public bool TryShip(string sku, int quantity)
	{
		if(quantity<=0||OnHand(sku)<quantity)
		{
			return false;
		}

		_onHand[sku] = OnHand(sku) - quantity;  
		return true;
	}
}
