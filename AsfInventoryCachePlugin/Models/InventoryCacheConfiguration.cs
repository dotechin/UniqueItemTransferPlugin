namespace AsfInventoryCachePlugin.Models;

public sealed class InventoryCacheConfiguration {
	public List<InventorySnapshot> Entries { get; init; } = [];
}
