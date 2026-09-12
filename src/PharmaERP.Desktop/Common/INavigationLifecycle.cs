using System.Threading;
using System.Threading.Tasks;

namespace PharmaERP.Desktop.Common;

/// <summary>
/// Contract for ViewModels that need to perform asynchronous data loading or lookup refresh upon navigation activation.
/// OnNavigatedToAsync manages its own refresh logic and preserves active user drafts.
/// </summary>
public interface IAsyncNavigable
{
    Task OnNavigatedToAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contract for ViewModels that support an explicit refresh operation (e.g. F5, Refresh button, or after data changes).
/// </summary>
public interface IRefreshableViewModel
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
