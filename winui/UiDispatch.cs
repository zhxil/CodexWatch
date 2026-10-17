using Microsoft.UI.Dispatching;
namespace CodexWatch;
internal static class UiDispatch
{
 public static DispatcherQueue Queue { get; set; } = null!;
 public static void Post(Action action) => Queue.TryEnqueue(() => action());
}
