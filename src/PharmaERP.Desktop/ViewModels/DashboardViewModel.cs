using System.Windows.Input;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class DashboardViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly IDashboardService _dashboardService;
    private readonly ConnectionStateStore _connectionStore;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private Action<string>? _navigateAction;

    private int _totalProducts;
    private int _totalManufacturers;
    private int _totalCategories;
    private int _totalUnits;

    // Real Sales Metrics (Requirement 16)
    private decimal _todaySalesTotal;
    private int _todayInvoiceCount;
    private decimal _todayReturnsTotal;
    private int _todayReturnCount;
    private decimal _todayNetSales;

    private bool _isLoading;
    private string? _errorMessage;

    public DashboardViewModel(
        IDashboardService dashboardService,
        ConnectionStateStore connectionStore,
        IUiDataChangeBus? eventBus = null,
        Action<string>? navigateAction = null)
    {
        _dashboardService = dashboardService;
        _connectionStore = connectionStore;
        _eventBus = eventBus;
        _navigateAction = navigateAction;

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadMetricsAsync(ct),
            canExecute: _ => !IsLoading);

        NavigateToCommand = new RelayCommand(
            execute: param =>
            {
                if (param is string destination)
                {
                    _navigateAction?.Invoke(destination);
                }
            });

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.SalePosted or UiDataChangeType.SaleReturned or UiDataChangeType.PurchasePosted or UiDataChangeType.PurchaseReturned or UiDataChangeType.ProductChanged or UiDataChangeType.StockChanged or UiDataChangeType.All)
                {
                    _ = LoadMetricsAsync(CancellationToken.None);
                }
            });
        }

        _ = LoadMetricsAsync(CancellationToken.None);
    }

    public void SetNavigateAction(Action<string> navigateAction)
    {
        _navigateAction = navigateAction;
    }

    public ConnectionStateStore ConnectionStore => _connectionStore;

    public int TotalProducts
    {
        get => _totalProducts;
        private set => SetField(ref _totalProducts, value);
    }

    public int TotalManufacturers
    {
        get => _totalManufacturers;
        private set => SetField(ref _totalManufacturers, value);
    }

    public int TotalCategories
    {
        get => _totalCategories;
        private set => SetField(ref _totalCategories, value);
    }

    public int TotalUnits
    {
        get => _totalUnits;
        private set => SetField(ref _totalUnits, value);
    }

    public decimal TodaySalesTotal
    {
        get => _todaySalesTotal;
        private set => SetField(ref _todaySalesTotal, value);
    }

    public int TodayInvoiceCount
    {
        get => _todayInvoiceCount;
        private set => SetField(ref _todayInvoiceCount, value);
    }

    public decimal TodayReturnsTotal
    {
        get => _todayReturnsTotal;
        private set => SetField(ref _todayReturnsTotal, value);
    }

    public int TodayReturnCount
    {
        get => _todayReturnCount;
        private set => SetField(ref _todayReturnCount, value);
    }

    public decimal TodayNetSales
    {
        get => _todayNetSales;
        private set => SetField(ref _todayNetSales, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand NavigateToCommand { get; }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        if (_connectionStore.IsConnected && _connectionStore.IsDatabaseInitialized)
        {
            _ = LoadMetricsAsync(CancellationToken.None);
        }
    }

    public async Task LoadMetricsAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected)
        {
            ErrorMessage = "Database is currently disconnected. Metrics cannot be retrieved.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var metrics = await _dashboardService.GetMetricsAsync(ct);
            TotalProducts = metrics.TotalProducts;
            TotalManufacturers = metrics.TotalManufacturers;
            TotalCategories = metrics.TotalCategories;
            TotalUnits = metrics.TotalUnits;

            TodaySalesTotal = metrics.TodaySalesTotal;
            TodayInvoiceCount = metrics.TodayInvoiceCount;
            TodayReturnsTotal = metrics.TodayReturnsTotal;
            TodayReturnCount = metrics.TodayReturnCount;
            TodayNetSales = metrics.TodayNetSales;

            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to load dashboard data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        await LoadMetricsAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await LoadMetricsAsync(ct);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionStore.ConnectionStateChanged -= OnConnectionStateChanged;
            _busSubscription?.Dispose();
        }

        base.Dispose(disposing);
    }
}

