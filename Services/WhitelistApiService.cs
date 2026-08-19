using ArchiSteamFarm.Steam.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UniqueItemTransferPlugin.Models;

namespace UniqueItemTransferPlugin.Services;

internal static class WhitelistApiService {
	internal static void MapEndpoints(IEndpointRouteBuilder routes) {
		RouteGroupBuilder group = routes.MapGroup("/api/UniqueItemTransfer");

		group.MapGet("/whitelist", GetWhitelist);
		group.MapGet("/whitelist/types", GetSupportedTypes);
		group.MapDelete("/whitelist/{index:int}", RemoveByIndex);
		group.MapDelete("/whitelist/classid/{classId:long}", RemoveByClassId);
		group.MapDelete("/whitelist", ClearWhitelist);
	}

	private static IResult GetWhitelist(HttpContext context) {
		string? typeFilter = context.Request.Query["type"].FirstOrDefault();
		int pageQuery = int.TryParse(context.Request.Query["page"].FirstOrDefault(), out int parsedPage) && parsedPage > 0 ? parsedPage : 1;
		int pageSizeQuery = int.TryParse(context.Request.Query["pageSize"].FirstOrDefault(), out int parsedSize) && parsedSize > 0 ? Math.Min(parsedSize, 200) : 50;

		WhitelistConfiguration config = TransferService.Instance.GetWhitelistConfiguration();

		// Build a list of (1-based index, entry) pairs, optionally filtered by type
		IEnumerable<(int OriginalIndex, WhitelistEntry Entry)> indexed = config.Entries
			.Select((e, i) => (OriginalIndex: i + 1, Entry: e));

		if (!string.IsNullOrEmpty(typeFilter) && Enum.TryParse(typeFilter, ignoreCase: true, out EAssetType filterType)) {
			indexed = indexed.Where(p => p.Entry.Type == filterType);
		}

		List<(int OriginalIndex, WhitelistEntry Entry)> filteredList = indexed.ToList();

		int totalCount = filteredList.Count;
		int totalPages = Math.Max(1, (int) Math.Ceiling(totalCount / (double) pageSizeQuery));
		int page = Math.Clamp(pageQuery, 1, totalPages);

		List<WhitelistEntryDto> pageEntries = filteredList
			.Skip((page - 1) * pageSizeQuery)
			.Take(pageSizeQuery)
			.Select(static p => new WhitelistEntryDto(p.OriginalIndex, p.Entry.RealAppID, p.Entry.Type.ToString(), p.Entry.ClassID, p.Entry.Name))
			.ToList();

		return Results.Ok(new WhitelistPageResult(pageEntries, page, totalPages, totalCount));
	}

	private static IResult GetSupportedTypes() {
		string[] types = [
			EAssetType.TradingCard.ToString(),
			EAssetType.FoilTradingCard.ToString(),
			EAssetType.ProfileBackground.ToString(),
			EAssetType.Emoticon.ToString()
		];

		return Results.Ok(types);
	}

	private static IResult RemoveByIndex(int index) {
		WhitelistEntry? removed = TransferService.Instance.RemoveWhitelistEntryByIndex(index);

		return removed is null
			? Results.NotFound(new { error = $"No whitelist entry at index {index}." })
			: Results.Ok(new { removed = new WhitelistEntryDto(index, removed.RealAppID, removed.Type.ToString(), removed.ClassID, removed.Name) });
	}

	private static IResult RemoveByClassId(long classId) {
		if (classId <= 0) {
			return Results.BadRequest(new { error = "classId must be a positive integer." });
		}

		int count = TransferService.Instance.RemoveWhitelistEntriesByClassId((ulong) classId);

		return count == 0
			? Results.NotFound(new { error = $"No whitelist entries found with classId {classId}." })
			: Results.Ok(new { removed = count });
	}

	private static IResult ClearWhitelist(HttpContext context) {
		string? confirm = context.Request.Query["confirm"].FirstOrDefault();

		if (!string.Equals(confirm, "true", StringComparison.OrdinalIgnoreCase)) {
			return Results.BadRequest(new { error = "Pass ?confirm=true to clear all whitelist entries." });
		}

		int count = TransferService.Instance.ClearWhitelist();

		return Results.Ok(new { cleared = count });
	}

	private sealed record WhitelistEntryDto(int Index, uint RealAppID, string Type, ulong ClassID, string? Name);
	private sealed record WhitelistPageResult(List<WhitelistEntryDto> Entries, int Page, int TotalPages, int TotalCount);
}
