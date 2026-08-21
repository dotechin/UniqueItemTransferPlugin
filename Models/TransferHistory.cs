namespace UniqueItemTransferPlugin.Models;

public enum TransferStatus {
	DryRun,
	Completed,
	Failed,
	PartialFailure,
	Expired
}

public sealed class TransferHistory {
	public List<TransferHistoryEntry> Entries { get; init; } = [];
}

public sealed class TransferHistoryEntry {
	public required Guid TransferId { get; init; }
	public required string SourceBotName { get; init; }
	public required string TargetBotName { get; init; }
	public required List<string> Modes { get; init; }
	public required bool DryRun { get; init; }
	public required TransferStatus Status { get; init; }
	public required DateTimeOffset CreatedAtUtc { get; init; }
	public required DateTimeOffset CompletedAtUtc { get; init; }
	public required int PlannedItemCount { get; init; }
	public required int CompletedBatchCount { get; init; }
	public required List<ulong> TradeOfferIds { get; init; }
	public string? ErrorMessage { get; init; }
}
