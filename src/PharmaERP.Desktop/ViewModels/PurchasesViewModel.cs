using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class PurchasesViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierService _supplierService;
    private readonly ConnectionStateStore _connectionStore;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private bool _isDirty = true;

    private readonly ObservableCollection<PurchaseInvoiceListDto> _invoices = [];
    private readonly ObservableCollection<LookupDto> _suppliers = [];
    private PurchaseInvoiceListDto? _selectedInvoice;
    private LookupDto? _selectedSupplierFilter;
    private string _searchTerm = string.Empty;

    private int _pageNumber = 1;
    private int _pageSize = 20;
    private int _totalCount;
    private int _totalPages;
    private bool _hasPreviousPage;
    private bool _hasNextPage;
    private string _pageInfoText = "No invoices";

    private bool _isLoading;
    private string? _errorMessage;

    // Details Drawer
    private bool _isDetailsDrawerOpen;
    private PurchaseInvoiceDto? _selectedInvoiceDetails;
    private bool _isLoadingDetails;

    // Cancellation Drawer / Dialog
    private bool _isCancelDrawerOpen;
    private string _cancelReason = string.Empty;
    private string? _cancelErrorMessage;
    private bool _isCancelling;

    private Action<string>? _navigateAction;
    private CancellationTokenSource? _searchDebounceCts;

    public PurchasesViewModel(
        IPurchaseService purchaseService,
        ISupplierService supplierService,
        ConnectionStateStore connectionStore,
        IUiDataChangeBus? eventBus = null)
    {
        _purchaseService = purchaseService;
        _supplierService = supplierService;
        _connectionStore = connectionStore;
        _eventBus = eventBus;

        Invoices = new ReadOnlyObservableCollection<PurchaseInvoiceListDto>(_invoices);
        Suppliers = new ReadOnlyObservableCollection<LookupDto>(_suppliers);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadDataAsync(ct),
            canExecute: _ => !IsLoading);

        FirstPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) => { PageNumber = 1; await LoadDataAsync(ct); },
            canExecute: _ => HasPreviousPage && !IsLoading);

        PreviousPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) => { if (PageNumber > 1) { PageNumber--; await LoadDataAsync(ct); } },
            canExecute: _ => HasPreviousPage && !IsLoading);

        NextPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) => { if (PageNumber < TotalPages) { PageNumber++; await LoadDataAsync(ct); } },
            canExecute: _ => HasNextPage && !IsLoading);

        LastPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) => { PageNumber = TotalPages; await LoadDataAsync(ct); },
            canExecute: _ => HasNextPage && !IsLoading);

        ViewDetailsCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                var target = (param as PurchaseInvoiceListDto) ?? SelectedInvoice;
                if (target != null) await OpenDetailsDrawerAsync(target.Id, ct);
            },
            canExecute: param => ((param as PurchaseInvoiceListDto) ?? SelectedInvoice) != null && !IsLoading);

        CloseDetailsCommand = new RelayCommand(
            execute: _ => IsDetailsDrawerOpen = false);

        OpenCancelDialogCommand = new RelayCommand(
            execute: param =>
            {
                var target = (param as PurchaseInvoiceListDto) ?? SelectedInvoice;
                if (target != null) OpenCancelDrawer(target);
            },
            canExecute: param =>
            {
                var target = (param as PurchaseInvoiceListDto) ?? SelectedInvoice;
                return target != null && target.Status == Domain.Entities.PurchaseInvoiceStatus.Posted && !IsLoading;
            });

        ConfirmCancelCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await CancelInvoiceAsync(ct),
            canExecute: _ => IsCancelDrawerOpen && !IsCancelling && !string.IsNullOrWhiteSpace(CancelReason));

        CloseCancelCommand = new RelayCommand(
            execute: _ => IsCancelDrawerOpen = false);

        NewPurchaseEntryCommand = new RelayCommand(
            execute: _ => _navigateAction?.Invoke("PurchaseEntry"));

        ClearFiltersCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                SearchTerm = string.Empty;
                SelectedSupplierFilter = null;
                PageNumber = 1;
                await LoadDataAsync(ct);
            });

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.PurchasePosted or UiDataChangeType.PurchaseReturned or UiDataChangeType.StockChanged or UiDataChangeType.SupplierChanged or UiDataChangeType.All)
                {
                    _isDirty = true;
                }
            });
        }

        _ = RefreshAllAsync(CancellationToken.None);
    }

    public void SetNavigateAction(Action<string> navigateAction)
    {
        _navigateAction = navigateAction;
    }

    public ConnectionStateStore ConnectionStore => _connectionStore;
    public ReadOnlyObservableCollection<PurchaseInvoiceListDto> Invoices { get; }
    public ReadOnlyObservableCollection<LookupDto> Suppliers { get; }

    public PurchaseInvoiceListDto? SelectedInvoice
    {
        get => _selectedInvoice;
        set => SetField(ref _selectedInvoice, value);
    }

    public LookupDto? SelectedSupplierFilter
    {
        get => _selectedSupplierFilter;
        set
        {
            if (SetField(ref _selectedSupplierFilter, value))
            {
                PageNumber = 1;
                _ = LoadDataAsync(CancellationToken.None);
            }
        }
    }

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (SetField(ref _searchTerm, value))
            {
                DebounceSearch();
            }
        }
    }

    public int PageNumber
    {
        get => _pageNumber;
        set => SetField(ref _pageNumber, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => SetField(ref _pageSize, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => SetField(ref _totalCount, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        private set => SetField(ref _totalPages, value);
    }

    public bool HasPreviousPage
    {
        get => _hasPreviousPage;
        private set => SetField(ref _hasPreviousPage, value);
    }

    public bool HasNextPage
    {
        get => _hasNextPage;
        private set => SetField(ref _hasNextPage, value);
    }

    public string PageInfoText
    {
        get => _pageInfoText;
        private set => SetField(ref _pageInfoText, value);
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

    // Details Drawer properties
    public bool IsDetailsDrawerOpen
    {
        get => _isDetailsDrawerOpen;
        set => SetField(ref _isDetailsDrawerOpen, value);
    }

    public PurchaseInvoiceDto? SelectedInvoiceDetails
    {
        get => _selectedInvoiceDetails;
        private set => SetField(ref _selectedInvoiceDetails, value);
    }

    public bool IsLoadingDetails
    {
        get => _isLoadingDetails;
        private set => SetField(ref _isLoadingDetails, value);
    }

    // Cancel Drawer properties
    public bool IsCancelDrawerOpen
    {
        get => _isCancelDrawerOpen;
        set => SetField(ref _isCancelDrawerOpen, value);
    }

    public string CancelReason
    {
        get => _cancelReason;
        set => SetField(ref _cancelReason, value);
    }

    public string? CancelErrorMessage
    {
        get => _cancelErrorMessage;
        private set => SetField(ref _cancelErrorMessage, value);
    }

    public bool IsCancelling
    {
        get => _isCancelling;
        private set => SetField(ref _isCancelling, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand ViewDetailsCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand OpenCancelDialogCommand { get; }
    public ICommand ConfirmCancelCommand { get; }
    public ICommand CloseCancelCommand { get; }
    public ICommand NewPurchaseEntryCommand { get; }
    public ICommand ClearFiltersCommand { get; }

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
                PageNumber = 1;
                await LoadDataAsync(token);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        if (_connectionStore.IsConnected && _connectionStore.IsDatabaseInitialized)
        {
            _ = RefreshAllAsync(CancellationToken.None);
        }
    }

    public async Task RefreshAllAsync(CancellationToken ct)
    {
        await LoadSuppliersAsync(ct);
        await LoadDataAsync(ct);
    }

    public async Task LoadSuppliersAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        try
        {
            var supps = await _supplierService.GetActiveLookupsAsync(ct);
            _suppliers.Clear();
            foreach (var s in supps) _suppliers.Add(s);
        }
        catch { }
    }

    public async Task LoadDataAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var query = new PurchaseInvoiceFilterQuery
            {
                PageNumber = PageNumber,
                PageSize = PageSize,
                SupplierId = SelectedSupplierFilter?.Id,
                SearchTerm = SearchTerm
            };

            var paged = await _purchaseService.GetPurchaseInvoicesPagedAsync(query, ct);

            _invoices.Clear();
            foreach (var item in paged.Items) _invoices.Add(item);

            TotalCount = paged.TotalCount;
            TotalPages = paged.TotalPages;
            HasPreviousPage = paged.HasPreviousPage;
            HasNextPage = paged.HasNextPage;

            if (TotalCount == 0)
            {
                PageInfoText = "No purchase invoices found";
            }
            else
            {
                int start = (PageNumber - 1) * PageSize + 1;
                int end = Math.Min(PageNumber * PageSize, TotalCount);
                PageInfoText = $"Showing {start}-{end} of {TotalCount} invoices (Page {PageNumber} of {TotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading invoices: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task OpenDetailsDrawerAsync(int invoiceId, CancellationToken ct)
    {
        IsLoadingDetails = true;
        IsDetailsDrawerOpen = true;

        try
        {
            SelectedInvoiceDetails = await _purchaseService.GetPurchaseInvoiceByIdAsync(invoiceId, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load invoice details: {ex.Message}";
            IsDetailsDrawerOpen = false;
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    public void OpenCancelDrawer(PurchaseInvoiceListDto invoice)
    {
        SelectedInvoice = invoice;
        CancelReason = string.Empty;
        CancelErrorMessage = null;
        IsCancelDrawerOpen = true;
    }

    public async Task CancelInvoiceAsync(CancellationToken ct)
    {
        if (SelectedInvoice == null) return;

        if (string.IsNullOrWhiteSpace(CancelReason))
        {
            CancelErrorMessage = "Please provide a reason for cancellation.";
            return;
        }

        IsCancelling = true;
        CancelErrorMessage = null;

        try
        {
            await _purchaseService.CancelPurchaseInvoiceAsync(SelectedInvoice.Id, CancelReason, ct);
            _eventBus?.Publish(UiDataChangeType.PurchaseCancelled);
            _eventBus?.Publish(UiDataChangeType.StockChanged);
            IsCancelDrawerOpen = false;
            await LoadDataAsync(ct);
        }
        catch (Exception ex)
        {
            CancelErrorMessage = ex.Message;
        }
        finally
        {
            IsCancelling = false;
        }
    }

    public async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        if (_isDirty)
        {
            _isDirty = false;
            await RefreshAllAsync(ct);
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _isDirty = false;
        await RefreshAllAsync(ct);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionStore.ConnectionStateChanged -= OnConnectionStateChanged;
            _busSubscription?.Dispose();
            _searchDebounceCts?.Dispose();
        }

        base.Dispose(disposing);
    }
}

