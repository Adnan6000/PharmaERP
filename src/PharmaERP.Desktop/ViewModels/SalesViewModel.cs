using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Desktop.ViewModels;

public class SalesViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly ISaleService _saleService;
    private readonly IInvoicePrintDataProvider _printDataProvider;
    private readonly ILogger<SalesViewModel> _logger;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private bool _isDirty = true;

    private DateOnly? _fromDate;
    private DateOnly? _toDate;
    private SaleType? _selectedSaleType;
    private SaleInvoiceStatus? _selectedStatus;
    private string _searchTerm = string.Empty;

    private ObservableCollection<SaleInvoiceDto> _invoices = new();
    private SaleInvoiceDto? _selectedInvoice;
    private SaleInvoiceDto? _selectedInvoiceDetails;
    private bool _isLoading;
    private string _statusMessage = string.Empty;

    public SalesViewModel(
        ISaleService saleService,
        IInvoicePrintDataProvider printDataProvider,
        ILogger<SalesViewModel> logger,
        IUiDataChangeBus? eventBus = null)
    {
        _saleService = saleService;
        _printDataProvider = printDataProvider;
        _logger = logger;
        _eventBus = eventBus;

        // Default to today
        _fromDate = DateOnly.FromDateTime(DateTime.UtcNow);
        _toDate = DateOnly.FromDateTime(DateTime.UtcNow);

        SearchCommand = new AsyncRelayCommand(LoadInvoicesAsync);
        FilterTodayCommand = new RelayCommand(_ => SetDateRange(0));
        FilterYesterdayCommand = new RelayCommand(_ => SetDateRange(1));
        FilterLast7DaysCommand = new RelayCommand(_ => SetDateRange(7));
        FilterAllTimeCommand = new RelayCommand(_ => ClearDateFilter());
        ReprintCommand = new AsyncRelayCommand(ReprintAsync, () => SelectedInvoice != null);
        CancelInvoiceCommand = new AsyncRelayCommand(CancelInvoiceAsync, () => SelectedInvoice != null && SelectedInvoice.Status == SaleInvoiceStatus.Posted);

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.SalePosted or UiDataChangeType.SaleReturned or UiDataChangeType.All)
                {
                    _isDirty = true;
                }
            });
        }

        _ = LoadInvoicesAsync();
    }

    #region Properties

    public DateOnly? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateOnly? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public SaleType? SelectedSaleType
    {
        get => _selectedSaleType;
        set => SetProperty(ref _selectedSaleType, value);
    }

    public SaleInvoiceStatus? SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
    }

    public string SearchTerm
    {
        get => _searchTerm;
        set => SetProperty(ref _searchTerm, value);
    }

    public ObservableCollection<SaleInvoiceDto> Invoices
    {
        get => _invoices;
        set => SetProperty(ref _invoices, value);
    }

    public SaleInvoiceDto? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            if (SetProperty(ref _selectedInvoice, value))
            {
                _ = LoadDetailsAsync(value?.Id);
            }
        }
    }

    public SaleInvoiceDto? SelectedInvoiceDetails
    {
        get => _selectedInvoiceDetails;
        set => SetProperty(ref _selectedInvoiceDetails, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand FilterTodayCommand { get; }
    public ICommand FilterYesterdayCommand { get; }
    public ICommand FilterLast7DaysCommand { get; }
    public ICommand FilterAllTimeCommand { get; }
    public ICommand ReprintCommand { get; }
    public ICommand CancelInvoiceCommand { get; }

    public event Action<InvoicePrintDataDto>? RequestShowPrintPreview;
    public event Func<string, string?>? RequestPromptInput;

    #endregion

    public async Task LoadInvoicesAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading sales history...";
        try
        {
            var list = await _saleService.SearchInvoicesAsync(
                FromDate, ToDate, null, SelectedSaleType, SelectedStatus, SearchTerm);

            Invoices = new ObservableCollection<SaleInvoiceDto>(list);
            StatusMessage = $"Found {list.Count} invoice(s).";
            if (list.Count > 0)
            {
                SelectedInvoice = list[0];
            }
            else
            {
                SelectedInvoiceDetails = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sales history.");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDetailsAsync(int? invoiceId)
    {
        if (!invoiceId.HasValue)
        {
            SelectedInvoiceDetails = null;
            return;
        }

        try
        {
            SelectedInvoiceDetails = await _saleService.GetByIdAsync(invoiceId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load invoice details for {Id}", invoiceId);
        }
    }

    private async Task ReprintAsync()
    {
        if (SelectedInvoice == null) return;
        try
        {
            var printData = await _printDataProvider.GetPrintDataAsync(SelectedInvoice.Id);
            RequestShowPrintPreview?.Invoke(printData);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reprint failed: {ex.Message}";
        }
    }

    private async Task CancelInvoiceAsync()
    {
        if (SelectedInvoice == null) return;

        string? reason = RequestPromptInput?.Invoke($"Enter cancellation reason for invoice {SelectedInvoice.InvoiceNumber}:");
        if (string.IsNullOrWhiteSpace(reason)) return;

        try
        {
            var cancelled = await _saleService.CancelSaleAsync(SelectedInvoice.Id, reason.Trim());
            StatusMessage = $"Invoice {cancelled.InvoiceNumber} cancelled successfully.";
            await LoadInvoicesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cancellation failed: {ex.Message}";
        }
    }

    private void SetDateRange(int daysBack)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (daysBack == 0)
        {
            FromDate = today;
            ToDate = today;
        }
        else if (daysBack == 1)
        {
            var yest = today.AddDays(-1);
            FromDate = yest;
            ToDate = yest;
        }
        else
        {
            FromDate = today.AddDays(-daysBack);
            ToDate = today;
        }

        _ = LoadInvoicesAsync();
    }

    private void ClearDateFilter()
    {
        FromDate = null;
        ToDate = null;
        _ = LoadInvoicesAsync();
    }

    public async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        if (_isDirty)
        {
            _isDirty = false;
            await LoadInvoicesAsync();
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _isDirty = false;
        await LoadInvoicesAsync();
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

