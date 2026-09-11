namespace UniqueItemTransferPlugin.Models;

public sealed class TransferRequest {
	public required Guid TransferId { get; init; }
	public required string SourceBotName { get; init; }
	public required string TargetBotName { get; init; }
	public required List<string> Modes { get; init; }
	public required bool Force { get; init; }
	public required int WhitelistedUniqueItemCount { get; init; }
	public required List<TransferBatch> Batches { get; init; }

	public int TotalItemCount => Batches.Sum(static batch => batch.ItemCount);
	public int BatchCount => Batches.Count;
}
