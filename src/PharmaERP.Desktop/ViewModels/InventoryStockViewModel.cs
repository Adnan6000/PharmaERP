using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class InventoryStockViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ConnectionStateStore _connectionStore;

    // Tabs: 0 = Current Stock, 1 = Batch Stock, 2 = Expiry Monitoring, 3 = Stock Movement Ledger
    private int _selectedTabIndex = 0;

    // Tab 1: Current Stock
    private readonly ObservableCollection<CurrentStockDto> _currentStockItems = [];
    private string _currentStockSearch = string.Empty;
    private int _currentStockPage = 1;
    private int _currentStockTotal;
    private int _currentStockTotalPages;
    private bool _currentStockHasPrev;
    private bool _currentStockHasNext;
    private string _currentStockPageInfo = "No items";

    // Tab 2: Batch Stock
    private readonly ObservableCollection<BatchStockDto> _batchStockItems = [];
    private string _batchStockSearch = string.Empty;
    private int _batchStockPage = 1;
    private int _batchStockTotal;
    private int _batchStockTotalPages;
    private bool _batchStockHasPrev;
    private bool _batchStockHasNext;
    private string _batchStockPageInfo = "No items";

    // Tab 3: Expiry Monitoring
    private readonly ObservableCollection<ExpiryReportItemDto> _expiryItems = [];
    private string _selectedExpiryFilter = "All"; // All, Expired, Near Expiry, Active
    private int _expiryThresholdDays = 90;
    private int _expiryPage = 1;
    private int _expiryTotal;
    private int _expiryTotalPages;
    private bool _expiryHasPrev;
    private bool _expiryHasNext;
    private string _expiryPageInfo = "No items";

    // Tab 4: Stock Movements
    private readonly ObservableCollection<StockMovementDto> _movementItems = [];
    private int _movementPage = 1;
    private int _movementTotal;
    private int _movementTotalPages;
    private bool _movementHasPrev;
    private bool _movementHasNext;
    private string _movementPageInfo = "No items";

    private bool _isLoading;
    private string? _errorMessage;

    private CancellationTokenSource? _searchDebounceCts;

    public InventoryStockViewModel(
        IInventoryService inventoryService,
        ConnectionStateStore connectionStore)
    {
        _inventoryService = inventoryService;
        _connectionStore = connectionStore;

        CurrentStockItems = new ReadOnlyObservableCollection<CurrentStockDto>(_currentStockItems);
        BatchStockItems = new ReadOnlyObservableCollection<BatchStockDto>(_batchStockItems);
        ExpiryItems = new ReadOnlyObservableCollection<ExpiryReportItemDto>(_expiryItems);
        MovementItems = new ReadOnlyObservableCollection<StockMovementDto>(_movementItems);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadCurrentTabAsync(ct),
            canExecute: _ => !IsLoading);

        // Pagination Commands
        FirstPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) => { SetCurrentTabPage(1); await LoadCurrentTabAsync(ct); },
            canExecute: _ => HasCurrentTabPrev() && !IsLoading);

        PreviousPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                int cur = GetCurrentTabPage();
                if (cur > 1) { SetCurrentTabPage(cur - 1); await LoadCurrentTabAsync(ct); }
            },
            canExecute: _ => HasCurrentTabPrev() && !IsLoading);

        NextPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                int cur = GetCurrentTabPage();
                int total = GetCurrentTabTotalPages();
                if (cur < total) { SetCurrentTabPage(cur + 1); await LoadCurrentTabAsync(ct); }
            },
            canExecute: _ => HasCurrentTabNext() && !IsLoading);

        LastPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) => { SetCurrentTabPage(GetCurrentTabTotalPages()); await LoadCurrentTabAsync(ct); },
            canExecute: _ => HasCurrentTabNext() && !IsLoading);

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;
        _ = LoadCurrentTabAsync(CancellationToken.None);
    }

    public ConnectionStateStore ConnectionStore => _connectionStore;

    public ReadOnlyObservableCollection<CurrentStockDto> CurrentStockItems { get; }
    public ReadOnlyObservableCollection<BatchStockDto> BatchStockItems { get; }
    public ReadOnlyObservableCollection<ExpiryReportItemDto> ExpiryItems { get; }
    public ReadOnlyObservableCollection<StockMovementDto> MovementItems { get; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetField(ref _selectedTabIndex, value))
            {
                _ = LoadCurrentTabAsync(CancellationToken.None);
            }
        }
    }

    // Tab 1 properties
    public string CurrentStockSearch
    {
        get => _currentStockSearch;
        set
        {
            if (SetField(ref _currentStockSearch, value))
            {
                DebounceSearch();
            }
        }
    }

    public string CurrentStockPageInfo
    {
        get => _currentStockPageInfo;
        private set => SetField(ref _currentStockPageInfo, value);
    }

    // Tab 2 properties
    public string BatchStockSearch
    {
        get => _batchStockSearch;
        set
        {
            if (SetField(ref _batchStockSearch, value))
            {
                DebounceSearch();
            }
        }
    }

    public string BatchStockPageInfo
    {
        get => _batchStockPageInfo;
        private set => SetField(ref _batchStockPageInfo, value);
    }

    // Tab 3 properties
    public string SelectedExpiryFilter
    {
        get => _selectedExpiryFilter;
        set
        {
            if (SetField(ref _selectedExpiryFilter, value))
            {
                _expiryPage = 1;
                _ = LoadExpiryAsync(CancellationToken.None);
            }
        }
    }

    public int ExpiryThresholdDays
    {
        get => _expiryThresholdDays;
        set
        {
            if (SetField(ref _expiryThresholdDays, value))
            {
                _expiryPage = 1;
                _ = LoadExpiryAsync(CancellationToken.None);
            }
        }
    }

    public string ExpiryPageInfo
    {
        get => _expiryPageInfo;
        private set => SetField(ref _expiryPageInfo, value);
    }

    // Tab 4 properties
    public string MovementPageInfo
    {
        get => _movementPageInfo;
        private set => SetField(ref _movementPageInfo, value);
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
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }

    private void DebounceSearch()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                SetCurrentTabPage(1);
                await LoadCurrentTabAsync(token);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        if (_connectionStore.IsConnected && _connectionStore.IsDatabaseInitialized)
        {
            _ = LoadCurrentTabAsync(CancellationToken.None);
        }
    }

    public async Task LoadCurrentTabAsync(CancellationToken ct)
    {
        switch (_selectedTabIndex)
        {
            case 0:
                await LoadCurrentStockAsync(ct);
                break;
            case 1:
                await LoadBatchStockAsync(ct);
                break;
            case 2:
                await LoadExpiryAsync(ct);
                break;
            case 3:
                await LoadMovementsAsync(ct);
                break;
        }
    }

    private async Task LoadCurrentStockAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var query = new PaginationQuery { PageNumber = _currentStockPage, PageSize = 20 };
            var result = await _inventoryService.GetCurrentStockPagedAsync(query, _currentStockSearch, null, null, ct);

            _currentStockItems.Clear();
            foreach (var item in result.Items) _currentStockItems.Add(item);

            _currentStockTotal = result.TotalCount;
            _currentStockTotalPages = result.TotalPages;
            _currentStockHasPrev = result.HasPreviousPage;
            _currentStockHasNext = result.HasNextPage;

            if (_currentStockTotal == 0)
            {
                CurrentStockPageInfo = "No products found in inventory";
            }
            else
            {
                int start = (_currentStockPage - 1) * 20 + 1;
                int end = Math.Min(_currentStockPage * 20, _currentStockTotal);
                CurrentStockPageInfo = $"Showing {start}-{end} of {_currentStockTotal} products (Page {_currentStockPage} of {_currentStockTotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading current stock: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadBatchStockAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var query = new PaginationQuery { PageNumber = _batchStockPage, PageSize = 20 };
            var result = await _inventoryService.GetBatchStockPagedAsync(query, _batchStockSearch, null, ct);

            _batchStockItems.Clear();
            foreach (var item in result.Items) _batchStockItems.Add(item);

            _batchStockTotal = result.TotalCount;
            _batchStockTotalPages = result.TotalPages;
            _batchStockHasPrev = result.HasPreviousPage;
            _batchStockHasNext = result.HasNextPage;

            if (_batchStockTotal == 0)
            {
                BatchStockPageInfo = "No batches found";
            }
            else
            {
                int start = (_batchStockPage - 1) * 20 + 1;
                int end = Math.Min(_batchStockPage * 20, _batchStockTotal);
                BatchStockPageInfo = $"Showing {start}-{end} of {_batchStockTotal} batches (Page {_batchStockPage} of {_batchStockTotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading batch stock: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadExpiryAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var query = new PaginationQuery { PageNumber = _expiryPage, PageSize = 20 };
            string? statusFilter = _selectedExpiryFilter == "All" ? null : _selectedExpiryFilter;
            var result = await _inventoryService.GetExpiryReportPagedAsync(query, _expiryThresholdDays, statusFilter, ct);

            _expiryItems.Clear();
            foreach (var item in result.Items) _expiryItems.Add(item);

            _expiryTotal = result.TotalCount;
            _expiryTotalPages = result.TotalPages;
            _expiryHasPrev = result.HasPreviousPage;
            _expiryHasNext = result.HasNextPage;

            if (_expiryTotal == 0)
            {
                ExpiryPageInfo = "No batches found for selected filter";
            }
            else
            {
                int start = (_expiryPage - 1) * 20 + 1;
                int end = Math.Min(_expiryPage * 20, _expiryTotal);
                ExpiryPageInfo = $"Showing {start}-{end} of {_expiryTotal} batches (Page {_expiryPage} of {_expiryTotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading expiry report: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadMovementsAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var query = new PaginationQuery { PageNumber = _movementPage, PageSize = 20 };
            var result = await _inventoryService.GetStockMovementsPagedAsync(query, null, null, ct);

            _movementItems.Clear();
            foreach (var item in result.Items) _movementItems.Add(item);

            _movementTotal = result.TotalCount;
            _movementTotalPages = result.TotalPages;
            _movementHasPrev = result.HasPreviousPage;
            _movementHasNext = result.HasNextPage;

            if (_movementTotal == 0)
            {
                MovementPageInfo = "No inventory movements recorded yet";
            }
            else
            {
                int start = (_movementPage - 1) * 20 + 1;
                int end = Math.Min(_movementPage * 20, _movementTotal);
                MovementPageInfo = $"Showing {start}-{end} of {_movementTotal} movements (Page {_movementPage} of {_movementTotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading stock movements: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private int GetCurrentTabPage() => _selectedTabIndex switch
    {
        0 => _currentStockPage,
        1 => _batchStockPage,
        2 => _expiryPage,
        3 => _movementPage,
        _ => 1
    };

    private void SetCurrentTabPage(int page)
    {
        switch (_selectedTabIndex)
        {
            case 0: _currentStockPage = page; break;
            case 1: _batchStockPage = page; break;
            case 2: _expiryPage = page; break;
            case 3: _movementPage = page; break;
        }
    }

    private int GetCurrentTabTotalPages() => _selectedTabIndex switch
    {
        0 => _currentStockTotalPages,
        1 => _batchStockTotalPages,
        2 => _expiryTotalPages,
        3 => _movementTotalPages,
        _ => 1
    };

    private bool HasCurrentTabPrev() => _selectedTabIndex switch
    {
        0 => _currentStockHasPrev,
        1 => _batchStockHasPrev,
        2 => _expiryHasPrev,
        3 => _movementHasPrev,
        _ => false
    };

    private bool HasCurrentTabNext() => _selectedTabIndex switch
    {
        0 => _currentStockHasNext,
        1 => _batchStockHasNext,
        2 => _expiryHasNext,
        3 => _movementHasNext,
        _ => false
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionStore.ConnectionStateChanged -= OnConnectionStateChanged;
            _searchDebounceCts?.Dispose();
        }

        base.Dispose(disposing);
    }
}

