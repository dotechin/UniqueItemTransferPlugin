using System;
using System.Threading.Tasks;

namespace UniqueItemTransferPlugin;

public class UniqueItemTransferPlugin {
	public string Name => "UniqueItemTransferPlugin";
	public Version Version => new(1, 0, 0, 0);

	public Task OnLoaded() {
		Console.WriteLine("✅ UniqueItemTransferPlugin loaded successfully!");
		return Task.CompletedTask;
	}
}