using ArchiSteamFarm.Steam.Data;
using UniqueItemTransferPlugin.Models;

namespace UniqueItemTransferPlugin.Services;

public sealed class BatchingService {
	public const ushort SafeBatchLimit = 390;

	public IReadOnlyList<TransferBatch> CreateBatches(IEnumerable<Asset> items) {
		ArgumentNullException.ThrowIfNull(items);

		List<TransferItem> serializedItems = items.Select(TransferItem.FromAsset).ToList();

		if (serializedItems.Count == 0) {
			return [];
		}

		List<TransferBatch> batches = [];

		for (int index = 0; index < serializedItems.Count; index += SafeBatchLimit) {
			batches.Add(new TransferBatch {
				BatchNumber = batches.Count + 1,
				Items = serializedItems.Skip(index).Take(SafeBatchLimit).ToList()
			});
		}

		return batches;
	}
}
