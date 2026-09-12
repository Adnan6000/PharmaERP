using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class SalesReturnsViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly ISaleReturnService _returnService;
    private readonly ISaleService _saleService;
    private readonly ILogger<SalesReturnsViewModel> _logger;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private bool _isDirty = true;

    // Search original invoice
    private string _invoiceSearchNumber = string.Empty;
    private SaleInvoiceDto? _loadedOriginalInvoice;

    // Return items to process
    private ObservableCollection<SalesReturnItemEntryViewModel> _returnItems = new();
    private string _returnReason = string.Empty;
    private decimal _totalRefundAmount = 0m;

    // Past returns history
    private ObservableCollection<SaleReturnDto> _pastReturns = new();
    private SaleReturnDto? _selectedPastReturn;

    private string _statusMessage = string.Empty;
    private bool _isError = false;
    private bool _isLoading = false;

    public SalesReturnsViewModel(
        ISaleReturnService returnService,
        ISaleService saleService,
        ILogger<SalesReturnsViewModel> logger,
        IUiDataChangeBus? eventBus = null)
    {
        _returnService = returnService;
        _saleService = saleService;
        _logger = logger;
        _eventBus = eventBus;

        SearchOriginalInvoiceCommand = new AsyncRelayCommand(SearchOriginalInvoiceAsync);
        PostReturnCommand = new AsyncRelayCommand(PostReturnAsync, () => ReturnItems.Any(i => i.ReturnQuantity > 0));
        CancelReturnCommand = new AsyncRelayCommand(CancelReturnAsync, () => SelectedPastReturn != null);
        RefreshHistoryCommand = new AsyncRelayCommand(LoadPastReturnsAsync);

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.SaleReturned or UiDataChangeType.SalePosted or UiDataChangeType.All)
                {
                    _isDirty = true;
                }
            });
        }

        _ = LoadPastReturnsAsync();
    }

    #region Properties

    public string InvoiceSearchNumber
    {
        get => _invoiceSearchNumber;
        set => SetProperty(ref _invoiceSearchNumber, value);
    }

    public SaleInvoiceDto? LoadedOriginalInvoice
    {
        get => _loadedOriginalInvoice;
        set => SetProperty(ref _loadedOriginalInvoice, value);
    }

    public ObservableCollection<SalesReturnItemEntryViewModel> ReturnItems
    {
        get => _returnItems;
        set => SetProperty(ref _returnItems, value);
    }

    public string ReturnReason
    {
        get => _returnReason;
        set => SetProperty(ref _returnReason, value);
    }

    public decimal TotalRefundAmount
    {
        get => _totalRefundAmount;
        set => SetProperty(ref _totalRefundAmount, value);
    }

    public ObservableCollection<SaleReturnDto> PastReturns
    {
        get => _pastReturns;
        set => SetProperty(ref _pastReturns, value);
    }

    public SaleReturnDto? SelectedPastReturn
    {
        get => _selectedPastReturn;
        set => SetProperty(ref _selectedPastReturn, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand SearchOriginalInvoiceCommand { get; }
    public ICommand PostReturnCommand { get; }
    public ICommand CancelReturnCommand { get; }
    public ICommand RefreshHistoryCommand { get; }

    public event Func<string, string?>? RequestPromptInput;

    #endregion

    public async Task SearchOriginalInvoiceAsync()
    {
        if (string.IsNullOrWhiteSpace(InvoiceSearchNumber)) return;

        IsLoading = true;
        StatusMessage = "Searching for invoice...";
        IsError = false;

        try
        {
            var matches = await _saleService.SearchInvoicesAsync(
                null, null, null, null, null, InvoiceSearchNumber.Trim(), take: 5);

            var matched = matches.FirstOrDefault(i =>
                string.Equals(i.InvoiceNumber, InvoiceSearchNumber.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                StatusMessage = $"Invoice '{InvoiceSearchNumber.Trim()}' was not found.";
                IsError = true;
                LoadedOriginalInvoice = null;
                ReturnItems.Clear();
                return;
            }

            var fullInvoice = await _saleService.GetByIdAsync(matched.Id);
            if (fullInvoice == null) return;

            LoadedOriginalInvoice = fullInvoice;

            var itemsList = new List<SalesReturnItemEntryViewModel>();
            foreach (var item in fullInvoice.Items)
            {
                decimal availableToReturn = Math.Max(0m, item.Quantity - item.ReturnedQuantity);
                itemsList.Add(new SalesReturnItemEntryViewModel(this)
                {
                    OriginalItemId = item.Id,
                    ProductId = item.ProductId,
                    ProductCode = item.ProductCodeSnapshot,
                    ProductName = item.ProductNameSnapshot,
                    Unit = item.UnitSnapshot,
                    SoldQuantity = item.Quantity,
                    AlreadyReturned = item.ReturnedQuantity,
                    AvailableToReturn = availableToReturn,
                    NetLineAmount = item.NetLineAmount,
                    RefundedAmount = item.RefundedAmount,
                    ReturnQuantity = 0m,
                    RefundAmount = 0m
                });
            }

            ReturnItems = new ObservableCollection<SalesReturnItemEntryViewModel>(itemsList);
            StatusMessage = $"Loaded invoice #{fullInvoice.InvoiceNumber} with {itemsList.Count} items.";
            IsError = false;
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search original invoice.");
            StatusMessage = $"Error: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RecalculateTotals()
    {
        TotalRefundAmount = ReturnItems.Sum(i => i.RefundAmount);
    }

    public async Task PostReturnAsync()
    {
        if (LoadedOriginalInvoice == null) return;

        var itemsToReturn = ReturnItems.Where(i => i.ReturnQuantity > 0).ToList();
        if (itemsToReturn.Count == 0)
        {
            StatusMessage = "Please specify a return quantity greater than 0 for at least one item.";
            IsError = true;
            return;
        }

        try
        {
            var createDto = new SaleReturnCreateDto
            {
                OperationId = Guid.NewGuid(),
                OriginalSaleInvoiceId = LoadedOriginalInvoice.Id,
                Reason = string.IsNullOrWhiteSpace(ReturnReason) ? null : ReturnReason.Trim(),
                Items = itemsToReturn.Select(i => new SaleReturnItemCreateDto
                {
                    OriginalSaleInvoiceItemId = i.OriginalItemId,
                    Quantity = i.ReturnQuantity
                }).ToList()
            };

            var posted = await _returnService.PostReturnAsync(createDto);
            StatusMessage = $"Return posted successfully! Return #{posted.ReturnNumber} for {AppCurrency.Format(posted.TotalAmount)}";
            IsError = false;
            _eventBus?.Publish(UiDataChangeType.SaleReturned);
            _eventBus?.Publish(UiDataChangeType.StockChanged);

            // Reset return form
            LoadedOriginalInvoice = null;
            ReturnItems.Clear();
            InvoiceSearchNumber = string.Empty;
            ReturnReason = string.Empty;
            TotalRefundAmount = 0m;

            await LoadPastReturnsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post sale return.");
            StatusMessage = $"Return failed: {ex.Message}";
            IsError = true;
        }
    }

    public async Task CancelReturnAsync()
    {
        if (SelectedPastReturn == null) return;

        string? reason = RequestPromptInput?.Invoke($"Enter cancellation reason for return {SelectedPastReturn.ReturnNumber}:");
        if (string.IsNullOrWhiteSpace(reason)) return;

        try
        {
            var cancelled = await _returnService.CancelReturnAsync(SelectedPastReturn.Id, reason.Trim());
            StatusMessage = $"Return {cancelled.ReturnNumber} cancelled successfully.";
            IsError = false;
            _eventBus?.Publish(UiDataChangeType.SaleReturned);
            _eventBus?.Publish(UiDataChangeType.StockChanged);
            await LoadPastReturnsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel return.");
            StatusMessage = $"Cancellation failed: {ex.Message}";
            IsError = true;
        }
    }

    public async Task LoadPastReturnsAsync()
    {
        try
        {
            var list = await _returnService.SearchReturnsAsync(null, null, null, null, take: 50);
            PastReturns = new ObservableCollection<SaleReturnDto>(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load return history.");
        }
    }

    public async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        if (_isDirty)
        {
            _isDirty = false;
            await LoadPastReturnsAsync();
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _isDirty = false;
        await LoadPastReturnsAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _busSubscription?.Dispose();
        }

        base.Dispose(disposing);
    }
}

public class SalesReturnItemEntryViewModel : ViewModelBase
{
    private readonly SalesReturnsViewModel _parent;
    private int _originalItemId;
    private int _productId;
    private string _productCode = string.Empty;
    private string _productName = string.Empty;
    private string _unit = string.Empty;
    private decimal _soldQuantity;
    private decimal _alreadyReturned;
    private decimal _availableToReturn;
    private decimal _netLineAmount;
    private decimal _refundedAmount;
    private decimal _returnQuantity;
    private decimal _refundAmount;

    public SalesReturnItemEntryViewModel(SalesReturnsViewModel parent)
    {
        _parent = parent;
    }

    public int OriginalItemId
    {
        get => _originalItemId;
        set => SetProperty(ref _originalItemId, value);
    }

    public int ProductId
    {
        get => _productId;
        set => SetProperty(ref _productId, value);
    }

    public string ProductCode
    {
        get => _productCode;
        set => SetProperty(ref _productCode, value);
    }

    public string ProductName
    {
        get => _productName;
        set => SetProperty(ref _productName, value);
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value);
    }

    public decimal SoldQuantity
    {
        get => _soldQuantity;
        set => SetProperty(ref _soldQuantity, value);
    }

    public decimal AlreadyReturned
    {
        get => _alreadyReturned;
        set => SetProperty(ref _alreadyReturned, value);
    }

    public decimal AvailableToReturn
    {
        get => _availableToReturn;
        set => SetProperty(ref _availableToReturn, value);
    }

    public decimal NetLineAmount
    {
        get => _netLineAmount;
        set => SetProperty(ref _netLineAmount, value);
    }

    public decimal RefundedAmount
    {
        get => _refundedAmount;
        set => SetProperty(ref _refundedAmount, value);
    }

    public decimal ReturnQuantity
    {
        get => _returnQuantity;
        set
        {
            decimal clamped = Math.Max(0m, Math.Min(AvailableToReturn, value));
            if (SetProperty(ref _returnQuantity, clamped))
            {
                RecalculateRefund();
                _parent.RecalculateTotals();
            }
        }
    }

    public decimal RefundAmount
    {
        get => _refundAmount;
        set => SetProperty(ref _refundAmount, value);
    }

    private void RecalculateRefund()
    {
        if (ReturnQuantity <= 0 || SoldQuantity <= 0)
        {
            RefundAmount = 0m;
            return;
        }

        // Check if this return would complete the return of the line
        if (AlreadyReturned + ReturnQuantity == SoldQuantity)
        {
            // Absorbs remaining rounding
            RefundAmount = Math.Max(0m, NetLineAmount - RefundedAmount);
        }
        else
        {
            decimal unitRate = NetLineAmount / SoldQuantity;
            RefundAmount = Math.Round(ReturnQuantity * unitRate, 4);
        }
    }
}

