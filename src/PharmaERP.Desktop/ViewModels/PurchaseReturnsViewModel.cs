using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class ReturnEntryLineItem : ViewModelBase
{
    private int _productId;
    private string _productName = string.Empty;
    private int _batchId;
    private string _batchNumber = string.Empty;
    private int? _originalInvoiceItemId;
    private decimal _maxReturnableQuantity;
    private decimal _quantity = 1m;
    private decimal _returnRate;
    private decimal _lineTotal;

    public int ProductId
    {
        get => _productId;
        set => SetField(ref _productId, value);
    }

    public string ProductName
    {
        get => _productName;
        set => SetField(ref _productName, value);
    }

    public int BatchId
    {
        get => _batchId;
        set => SetField(ref _batchId, value);
    }

    public string BatchNumber
    {
        get => _batchNumber;
        set => SetField(ref _batchNumber, value);
    }

    public int? OriginalInvoiceItemId
    {
        get => _originalInvoiceItemId;
        set => SetField(ref _originalInvoiceItemId, value);
    }

    public decimal MaxReturnableQuantity
    {
        get => _maxReturnableQuantity;
        set => SetField(ref _maxReturnableQuantity, value);
    }

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (SetField(ref _quantity, value))
            {
                Recalculate();
            }
        }
    }

    public decimal ReturnRate
    {
        get => _returnRate;
        set
        {
            if (SetField(ref _returnRate, value))
            {
                Recalculate();
            }
        }
    }

    public decimal LineTotal
    {
        get => _lineTotal;
        private set => SetField(ref _lineTotal, value);
    }

    public Action? OnLineChanged { get; set; }

    public void Recalculate()
    {
        LineTotal = Quantity * ReturnRate;
        OnLineChanged?.Invoke();
    }
}

public class PurchaseReturnsViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierService _supplierService;
    private readonly ConnectionStateStore _connectionStore;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private bool _isDirty = true;

    private readonly ObservableCollection<PurchaseReturnListDto> _returns = [];
    private readonly ObservableCollection<LookupDto> _suppliers = [];
    private readonly ObservableCollection<PurchaseInvoiceListDto> _supplierInvoices = [];
    private readonly ObservableCollection<ReturnEntryLineItem> _returnItems = [];

    private PurchaseReturnListDto? _selectedReturn;
    private LookupDto? _selectedSupplierFilter;
    private string _searchTerm = string.Empty;

    private int _pageNumber = 1;
    private int _pageSize = 20;
    private int _totalCount;
    private int _totalPages;
    private bool _hasPreviousPage;
    private bool _hasNextPage;
    private string _pageInfoText = "No returns";

    private bool _isLoading;
    private string? _errorMessage;

    // Create Return Drawer
    private bool _isCreateDrawerOpen;
    private LookupDto? _formSupplier;
    private PurchaseInvoiceListDto? _formOriginalInvoice;
    private DateTime _formReturnDate = DateTime.Today;
    private string? _formReason;
    private decimal _formTotalAmount;
    private bool _isSaving;
    private string? _formErrorMessage;

    // Details Drawer
    private bool _isDetailsDrawerOpen;
    private PurchaseReturnDto? _selectedReturnDetails;
    private bool _isLoadingDetails;

    // Cancel Return Drawer / Dialog
    private bool _isCancelDrawerOpen;
    private string _cancelReason = string.Empty;
    private string? _cancelErrorMessage;
    private bool _isCancelling;
    private PurchaseReturnListDto? _returnToCancel;

    public PurchaseReturnsViewModel(
        IPurchaseService purchaseService,
        ISupplierService supplierService,
        ConnectionStateStore connectionStore,
        IUiDataChangeBus? eventBus = null)
    {
        _purchaseService = purchaseService;
        _supplierService = supplierService;
        _connectionStore = connectionStore;
        _eventBus = eventBus;

        Returns = new ReadOnlyObservableCollection<PurchaseReturnListDto>(_returns);
        Suppliers = new ReadOnlyObservableCollection<LookupDto>(_suppliers);
        SupplierInvoices = new ReadOnlyObservableCollection<PurchaseInvoiceListDto>(_supplierInvoices);
        ReturnItems = new ReadOnlyObservableCollection<ReturnEntryLineItem>(_returnItems);

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

        OpenCreateDrawerCommand = new RelayCommand(
            execute: _ => OpenCreateDrawer(),
            canExecute: _ => !IsLoading && !IsSaving);

        CloseCreateDrawerCommand = new RelayCommand(
            execute: _ => IsCreateDrawerOpen = false);

        ViewDetailsCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                var target = (param as PurchaseReturnListDto) ?? SelectedReturn;
                if (target != null) await OpenDetailsDrawerAsync(target.Id, ct);
            },
            canExecute: param => ((param as PurchaseReturnListDto) ?? SelectedReturn) != null && !IsLoading);

        CloseDetailsCommand = new RelayCommand(
            execute: _ => IsDetailsDrawerOpen = false);

        SaveReturnCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveReturnAsync(ct),
            canExecute: _ => IsCreateDrawerOpen && !IsSaving && FormSupplier != null && _returnItems.Count > 0);

        OpenCancelDialogCommand = new RelayCommand(
            execute: param =>
            {
                var target = (param as PurchaseReturnListDto) ?? SelectedReturn;
                if (target != null) OpenCancelDrawer(target);
            },
            canExecute: param =>
            {
                var target = (param as PurchaseReturnListDto) ?? SelectedReturn;
                return target != null && target.Status == Domain.Entities.PurchaseReturnStatus.Posted && !IsLoading;
            });

        ConfirmCancelCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await ConfirmCancelReturnAsync(ct),
            canExecute: _ => IsCancelDrawerOpen && !IsCancelling && !string.IsNullOrWhiteSpace(CancelReason));

        CloseCancelCommand = new RelayCommand(
            execute: _ => IsCancelDrawerOpen = false);

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.PurchaseReturned or UiDataChangeType.PurchasePosted or UiDataChangeType.StockChanged or UiDataChangeType.SupplierChanged or UiDataChangeType.All)
                {
                    _isDirty = true;
                }
            });
        }

        _ = RefreshAllAsync(CancellationToken.None);
    }

    public ConnectionStateStore ConnectionStore => _connectionStore;
    public ReadOnlyObservableCollection<PurchaseReturnListDto> Returns { get; }
    public ReadOnlyObservableCollection<LookupDto> Suppliers { get; }
    public ReadOnlyObservableCollection<PurchaseInvoiceListDto> SupplierInvoices { get; }
    public ReadOnlyObservableCollection<ReturnEntryLineItem> ReturnItems { get; }

    public PurchaseReturnListDto? SelectedReturn
    {
        get => _selectedReturn;
        set => SetField(ref _selectedReturn, value);
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
        set => SetField(ref _searchTerm, value);
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

    // Create Drawer Properties
    public bool IsCreateDrawerOpen
    {
        get => _isCreateDrawerOpen;
        set => SetField(ref _isCreateDrawerOpen, value);
    }

    public LookupDto? FormSupplier
    {
        get => _formSupplier;
        set
        {
            if (SetField(ref _formSupplier, value))
            {
                _ = OnFormSupplierChangedAsync(value);
            }
        }
    }

    public PurchaseInvoiceListDto? FormOriginalInvoice
    {
        get => _formOriginalInvoice;
        set
        {
            if (SetField(ref _formOriginalInvoice, value))
            {
                _ = OnFormInvoiceChangedAsync(value);
            }
        }
    }

    public DateTime FormReturnDate
    {
        get => _formReturnDate;
        set => SetField(ref _formReturnDate, value);
    }

    public string? FormReason
    {
        get => _formReason;
        set => SetField(ref _formReason, value);
    }

    public decimal FormTotalAmount
    {
        get => _formTotalAmount;
        private set => SetField(ref _formTotalAmount, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetField(ref _isSaving, value);
    }

    public string? FormErrorMessage
    {
        get => _formErrorMessage;
        private set => SetField(ref _formErrorMessage, value);
    }

    // Details Drawer Properties
    public bool IsDetailsDrawerOpen
    {
        get => _isDetailsDrawerOpen;
        set => SetField(ref _isDetailsDrawerOpen, value);
    }

    public PurchaseReturnDto? SelectedReturnDetails
    {
        get => _selectedReturnDetails;
        private set => SetField(ref _selectedReturnDetails, value);
    }

    public bool IsLoadingDetails
    {
        get => _isLoadingDetails;
        private set => SetField(ref _isLoadingDetails, value);
    }

    // Cancel Drawer Properties
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
    public ICommand OpenCreateDrawerCommand { get; }
    public ICommand CloseCreateDrawerCommand { get; }
    public ICommand ViewDetailsCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand SaveReturnCommand { get; }
    public ICommand OpenCancelDialogCommand { get; }
    public ICommand ConfirmCancelCommand { get; }
    public ICommand CloseCancelCommand { get; }

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
            var query = new PurchaseReturnFilterQuery
            {
                PageNumber = PageNumber,
                PageSize = PageSize,
                SupplierId = SelectedSupplierFilter?.Id,
                SearchTerm = SearchTerm
            };

            var paged = await _purchaseService.GetPurchaseReturnsPagedAsync(query, ct);

            _returns.Clear();
            foreach (var item in paged.Items) _returns.Add(item);

            TotalCount = paged.TotalCount;
            TotalPages = paged.TotalPages;
            HasPreviousPage = paged.HasPreviousPage;
            HasNextPage = paged.HasNextPage;

            if (TotalCount == 0)
            {
                PageInfoText = "No purchase returns found";
            }
            else
            {
                int start = (PageNumber - 1) * PageSize + 1;
                int end = Math.Min(PageNumber * PageSize, TotalCount);
                PageInfoText = $"Showing {start}-{end} of {TotalCount} returns (Page {PageNumber} of {TotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading returns: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OpenCreateDrawer()
    {
        FormSupplier = null;
        FormOriginalInvoice = null;
        _supplierInvoices.Clear();
        _returnItems.Clear();
        FormReturnDate = DateTime.Today;
        FormReason = null;
        FormTotalAmount = 0.00m;
        FormErrorMessage = null;
        IsCreateDrawerOpen = true;
    }

    private async Task OnFormSupplierChangedAsync(LookupDto? supplier)
    {
        _supplierInvoices.Clear();
        _returnItems.Clear();
        FormTotalAmount = 0.00m;

        if (supplier == null) return;

        try
        {
            var paged = await _purchaseService.GetPurchaseInvoicesPagedAsync(new PurchaseInvoiceFilterQuery
            {
                SupplierId = supplier.Id,
                Status = Domain.Entities.PurchaseInvoiceStatus.Posted,
                PageNumber = 1,
                PageSize = 100
            });

            foreach (var inv in paged.Items)
            {
                _supplierInvoices.Add(inv);
            }
        }
        catch { }
    }

    private async Task OnFormInvoiceChangedAsync(PurchaseInvoiceListDto? invoice)
    {
        _returnItems.Clear();
        FormTotalAmount = 0.00m;

        if (invoice == null) return;

        try
        {
            var full = await _purchaseService.GetPurchaseInvoiceByIdAsync(invoice.Id);
            if (full != null)
            {
                foreach (var line in full.Items.Where(i => i.RemainingReturnableQuantity > 0))
                {
                    var item = new ReturnEntryLineItem
                    {
                        ProductId = line.ProductId,
                        ProductName = line.ProductName,
                        BatchId = line.ProductBatchId,
                        BatchNumber = line.BatchNumber,
                        OriginalInvoiceItemId = line.Id,
                        MaxReturnableQuantity = line.RemainingReturnableQuantity,
                        Quantity = 0m,
                        ReturnRate = line.PurchaseRate,
                        OnLineChanged = RecalculateTotal
                    };
                    _returnItems.Add(item);
                }
            }
        }
        catch { }
    }

    private void RecalculateTotal()
    {
        decimal total = 0m;
        foreach (var item in _returnItems)
        {
            total += item.LineTotal;
        }
        FormTotalAmount = total;
    }

    public async Task SaveReturnAsync(CancellationToken ct)
    {
        if (FormSupplier == null)
        {
            FormErrorMessage = "Please select a supplier.";
            return;
        }

        var linesToReturn = _returnItems.Where(i => i.Quantity > 0).ToList();
        if (linesToReturn.Count == 0)
        {
            FormErrorMessage = "Please enter return quantity (> 0) on at least one item.";
            return;
        }

        foreach (var line in linesToReturn)
        {
            if (line.OriginalInvoiceItemId.HasValue && line.Quantity > line.MaxReturnableQuantity)
            {
                FormErrorMessage = $"Quantity for '{line.ProductName}' exceeds returnable limit ({line.MaxReturnableQuantity}).";
                return;
            }
        }

        IsSaving = true;
        FormErrorMessage = null;

        var dto = new PurchaseReturnCreateDto
        {
            SupplierId = FormSupplier.Id,
            OriginalPurchaseInvoiceId = FormOriginalInvoice?.Id,
            ReturnDate = DateOnly.FromDateTime(FormReturnDate),
            Reason = FormReason?.Trim(),
            Items = linesToReturn.Select(l => new PurchaseReturnItemCreateDto
            {
                OriginalPurchaseInvoiceItemId = l.OriginalInvoiceItemId,
                ProductId = l.ProductId,
                ProductBatchId = l.BatchId,
                Quantity = l.Quantity,
                ReturnRate = l.ReturnRate
            }).ToList()
        };

        try
        {
            await _purchaseService.CreateAndPostPurchaseReturnAsync(dto, ct);
            _eventBus?.Publish(UiDataChangeType.PurchaseReturned);
            _eventBus?.Publish(UiDataChangeType.StockChanged);
            IsCreateDrawerOpen = false;
            await LoadDataAsync(ct);
        }
        catch (Exception ex)
        {
            FormErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task OpenDetailsDrawerAsync(int returnId, CancellationToken ct)
    {
        IsLoadingDetails = true;
        IsDetailsDrawerOpen = true;

        try
        {
            SelectedReturnDetails = await _purchaseService.GetPurchaseReturnByIdAsync(returnId, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading return details: {ex.Message}";
            IsDetailsDrawerOpen = false;
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    public void OpenCancelDrawer(PurchaseReturnListDto returnDto)
    {
        _returnToCancel = returnDto;
        CancelReason = string.Empty;
        CancelErrorMessage = null;
        IsCancelDrawerOpen = true;
    }

    public async Task ConfirmCancelReturnAsync(CancellationToken ct)
    {
        if (_returnToCancel == null) return;

        if (string.IsNullOrWhiteSpace(CancelReason))
        {
            CancelErrorMessage = "Please provide a reason for cancellation.";
            return;
        }

        IsCancelling = true;
        CancelErrorMessage = null;

        try
        {
            await _purchaseService.CancelPurchaseReturnAsync(_returnToCancel.Id, CancelReason, ct);
            _eventBus?.Publish(UiDataChangeType.PurchaseReturned);
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
        }

        base.Dispose(disposing);
    }
}

