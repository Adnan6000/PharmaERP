using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Models;
using PharmaERP.Desktop.Services;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Desktop.ViewModels;

public class SalesEntryViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly ISaleService _saleService;
    private readonly IProductLookupService _lookupService;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInvoicePrintDataProvider _printDataProvider;
    private readonly InvoicePrintService _printService;
    private readonly WorkstationConfigService _configService;
    private readonly ILogger<SalesEntryViewModel> _logger;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private bool _customersDirty = true;

    // Search and Input fields
    private string _searchTerm = string.Empty;
    private string _lookupStatusMessage = string.Empty;
    private bool _isLookupError = false;
    private ObservableCollection<ProductLookupResultDto> _searchSuggestions = new();
    private bool _isSuggestionPopupOpen = false;
    private ProductLookupResultDto? _selectedSuggestion;

    // Customer & Header Details
    private ObservableCollection<CustomerDto> _customers = new();
    private CustomerDto? _selectedCustomer;
    private SaleType _selectedSaleType = SaleType.Cash;
    private string _reference = string.Empty;
    private string _remarks = string.Empty;
    private decimal _invoiceDiscountAmount = 0m;

    // Cart Items
    private ObservableCollection<SalesCartItemViewModel> _items = new();
    private SalesCartItemViewModel? _selectedItem;

    // Tender Modal State
    private bool _isTenderModalOpen = false;
    private decimal _tenderedAmount = 0m;
    private decimal _changeGiven = 0m;
    private bool _printReceiptOnSave = true;

    // Parked tickets
    private readonly List<ParkedTicket> _parkedTickets = new();
    private string _parkedStatusText = string.Empty;

    public SalesEntryViewModel(
        ISaleService saleService,
        IProductLookupService lookupService,
        ICustomerRepository customerRepository,
        IInvoicePrintDataProvider printDataProvider,
        InvoicePrintService printService,
        WorkstationConfigService configService,
        ILogger<SalesEntryViewModel> logger,
        IUiDataChangeBus? eventBus = null)
    {
        _saleService = saleService;
        _lookupService = lookupService;
        _customerRepository = customerRepository;
        _printDataProvider = printDataProvider;
        _printService = printService;
        _configService = configService;
        _logger = logger;
        _eventBus = eventBus;

        // Commands
        SearchOrScanCommand = new AsyncRelayCommand(SearchOrScanAsync);
        DeleteItemCommand = new RelayCommand(DeleteItem, _ => SelectedItem != null);
        ClearFormCommand = new RelayCommand(_ => ClearAll());
        OpenTenderModalCommand = new RelayCommand(_ => OpenTenderModal(), _ => Items.Count > 0);
        CloseTenderModalCommand = new RelayCommand(_ => IsTenderModalOpen = false);
        CompleteSaleCommand = new AsyncRelayCommand(CompleteSaleAsync, () => Items.Count > 0);
        QuickCashSaleCommand = new AsyncRelayCommand(QuickCashSaleAsync, () => Items.Count > 0);
        ParkTicketCommand = new RelayCommand(_ => ParkCurrentTicket(), _ => Items.Count > 0);
        RecallTicketCommand = new RelayCommand(_ => RecallParkedTicket(), _ => _parkedTickets.Count > 0);
        FocusSearchCommand = new RelayCommand(_ => RequestFocusSearch?.Invoke());
        EscapeCommand = new RelayCommand(_ => HandleEscape());
        SelectSuggestionCommand = new RelayCommand(param => SelectSuggestion(param as ProductLookupResultDto ?? SelectedSuggestion));
        CloseSuggestionsCommand = new RelayCommand(_ => { IsSuggestionPopupOpen = false; SearchSuggestions.Clear(); });

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.CustomerChanged or UiDataChangeType.MasterChanged or UiDataChangeType.All)
                {
                    _customersDirty = true;
                }
            });
        }

        _ = LoadCustomersAsync();
    }

    #region Properties

    public string SearchTerm
    {
        get => _searchTerm;
        set => SetProperty(ref _searchTerm, value);
    }

    public string LookupStatusMessage
    {
        get => _lookupStatusMessage;
        set => SetProperty(ref _lookupStatusMessage, value);
    }

    public bool IsLookupError
    {
        get => _isLookupError;
        set => SetProperty(ref _isLookupError, value);
    }

    public ObservableCollection<CustomerDto> Customers
    {
        get => _customers;
        set => SetProperty(ref _customers, value);
    }

    public CustomerDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                if (value != null && SelectedSaleType == SaleType.Cash)
                {
                    // Keep cash unless user explicitly toggles credit
                }
            }
        }
    }

    public SaleType SelectedSaleType
    {
        get => _selectedSaleType;
        set
        {
            if (SetProperty(ref _selectedSaleType, value))
            {
                OnPropertyChanged(nameof(IsCreditSale));
                OnPropertyChanged(nameof(IsCashSale));
            }
        }
    }

    public bool IsCashSale
    {
        get => SelectedSaleType == SaleType.Cash;
        set { if (value) SelectedSaleType = SaleType.Cash; }
    }

    public bool IsCreditSale
    {
        get => SelectedSaleType == SaleType.Credit;
        set { if (value) SelectedSaleType = SaleType.Credit; }
    }

    public string Reference
    {
        get => _reference;
        set => SetProperty(ref _reference, value);
    }

    public string Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    public decimal InvoiceDiscountAmount
    {
        get => _invoiceDiscountAmount;
        set
        {
            if (SetProperty(ref _invoiceDiscountAmount, Math.Max(0m, value)))
            {
                RecalculateTotals();
            }
        }
    }

    public ObservableCollection<SalesCartItemViewModel> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    public SalesCartItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    // Totals
    public decimal GrossTotal => Items.Sum(i => i.GrossAmount);
    public decimal LineDiscountTotal => Items.Sum(i => i.LineDiscountAmount);
    public decimal NetTotal => Math.Max(0m, GrossTotal - LineDiscountTotal - InvoiceDiscountAmount);

    // Tender Modal Properties
    public bool IsTenderModalOpen
    {
        get => _isTenderModalOpen;
        set => SetProperty(ref _isTenderModalOpen, value);
    }

    public decimal TenderedAmount
    {
        get => _tenderedAmount;
        set
        {
            if (SetProperty(ref _tenderedAmount, Math.Max(0m, value)))
            {
                ChangeGiven = Math.Max(0m, _tenderedAmount - NetTotal);
            }
        }
    }

    public decimal ChangeGiven
    {
        get => _changeGiven;
        set => SetProperty(ref _changeGiven, value);
    }

    public bool PrintReceiptOnSave
    {
        get => _printReceiptOnSave;
        set => SetProperty(ref _printReceiptOnSave, value);
    }

    public string ParkedStatusText
    {
        get => _parkedStatusText;
        set => SetProperty(ref _parkedStatusText, value);
    }

    public int ParkedTicketCount => _parkedTickets.Count;

    public ObservableCollection<ProductLookupResultDto> SearchSuggestions
    {
        get => _searchSuggestions;
        set => SetProperty(ref _searchSuggestions, value);
    }

    public bool IsSuggestionPopupOpen
    {
        get => _isSuggestionPopupOpen;
        set => SetProperty(ref _isSuggestionPopupOpen, value);
    }

    public ProductLookupResultDto? SelectedSuggestion
    {
        get => _selectedSuggestion;
        set => SetProperty(ref _selectedSuggestion, value);
    }

    #endregion

    #region Commands

    public ICommand SearchOrScanCommand { get; }
    public ICommand DeleteItemCommand { get; }
    public ICommand ClearFormCommand { get; }
    public ICommand OpenTenderModalCommand { get; }
    public ICommand CloseTenderModalCommand { get; }
    public ICommand CompleteSaleCommand { get; }
    public ICommand QuickCashSaleCommand { get; }
    public ICommand ParkTicketCommand { get; }
    public ICommand RecallTicketCommand { get; }
    public ICommand FocusSearchCommand { get; }
    public ICommand EscapeCommand { get; }
    public ICommand SelectSuggestionCommand { get; }
    public ICommand CloseSuggestionsCommand { get; }

    public event Action? RequestFocusSearch;
    public event Action<InvoicePrintDataDto>? RequestShowPrintPreview;

    #endregion

    public async Task SearchOrScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm)) return;

        string term = SearchTerm.Trim();
        SearchTerm = string.Empty;

        // 1. First attempt exact barcode or code match
        var exactResult = await _lookupService.LookupByBarcodeOrCodeAsync(term);
        if (exactResult.Status == ProductLookupStatus.Available && exactResult.Product != null)
        {
            IsSuggestionPopupOpen = false;
            SearchSuggestions.Clear();
            LookupStatusMessage = exactResult.Message;
            IsLookupError = false;
            AddOrIncrementProduct(exactResult);
            RequestFocusSearch?.Invoke();
            return;
        }
        else if (exactResult.Status != ProductLookupStatus.NotFound && exactResult.Product != null)
        {
            // Exact code/barcode matched, but product is inactive or out of stock
            IsSuggestionPopupOpen = false;
            SearchSuggestions.Clear();
            LookupStatusMessage = exactResult.Message;
            IsLookupError = true;
            RequestFocusSearch?.Invoke();
            return;
        }

        // 2. Exact code/barcode not found: search by product name / generic name
        var searchResults = await _lookupService.SearchProductsAsync(term, maxResults: 15);
        if (searchResults.Count == 0)
        {
            IsSuggestionPopupOpen = false;
            SearchSuggestions.Clear();
            LookupStatusMessage = $"No products matching '{term}' were found.";
            IsLookupError = true;
        }
        else if (searchResults.Count == 1)
        {
            var single = searchResults[0];
            IsSuggestionPopupOpen = false;
            SearchSuggestions.Clear();
            if (single.Status == ProductLookupStatus.Available && single.Product != null)
            {
                AddOrIncrementProduct(single);
                LookupStatusMessage = single.Message;
                IsLookupError = false;
            }
            else
            {
                LookupStatusMessage = single.Message;
                IsLookupError = true;
            }
        }
        else
        {
            // Multiple matches found: display popup suggestions for cashier disambiguation
            SearchSuggestions = new ObservableCollection<ProductLookupResultDto>(searchResults);
            IsSuggestionPopupOpen = true;
            LookupStatusMessage = $"Found {searchResults.Count} matches for '{term}'. Select a product from the list.";
            IsLookupError = false;
        }

        RequestFocusSearch?.Invoke();
    }

    public void SelectSuggestion(ProductLookupResultDto? suggestion)
    {
        if (suggestion == null) return;

        IsSuggestionPopupOpen = false;
        SearchSuggestions.Clear();

        if (suggestion.Status == ProductLookupStatus.Available && suggestion.Product != null)
        {
            AddOrIncrementProduct(suggestion);
            LookupStatusMessage = suggestion.Message;
            IsLookupError = false;
        }
        else
        {
            LookupStatusMessage = suggestion.Message;
            IsLookupError = true;
        }

        RequestFocusSearch?.Invoke();
    }

    private void AddOrIncrementProduct(ProductLookupResultDto lookup)
    {
        var prod = lookup.Product!;

        // Duplicate scan aggregation: if item is already in cart, increment quantity
        var existing = Items.FirstOrDefault(i => i.ProductId == prod.Id);
        if (existing != null)
        {
            existing.Quantity += 1;
            existing.Recalculate();
            RecalculateTotals();
            LookupStatusMessage = $"Incremented '{prod.Name}' to {existing.Quantity:0.##} units.";
            IsLookupError = false;
            return;
        }

        var cartItem = new SalesCartItemViewModel(this)
        {
            LineNumber = Items.Count + 1,
            ProductId = prod.Id,
            ProductCode = prod.ProductCode ?? string.Empty,
            ProductName = prod.Name,
            Unit = prod.UnitAbbreviation ?? "Unit",
            Quantity = 1,
            UnitSalePrice = lookup.DefaultSalePrice > 0 ? lookup.DefaultSalePrice : prod.DefaultSalePrice,
            LineDiscountAmount = 0m,
            AvailableStock = lookup.TotalSaleableQuantity
        };

        if (lookup.EligibleBatches.Count > 0)
        {
            var firstExp = lookup.EligibleBatches.First();
            cartItem.AllocationsPreview = $"FEFO: {firstExp.BatchNumber} (Exp: {firstExp.ExpiryDate:MM/yy})";
        }

        cartItem.Recalculate();
        Items.Add(cartItem);
        SelectedItem = cartItem;
        RecalculateTotals();
    }

    public void RecalculateTotals()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].LineNumber = i + 1;
        }

        OnPropertyChanged(nameof(GrossTotal));
        OnPropertyChanged(nameof(LineDiscountTotal));
        OnPropertyChanged(nameof(NetTotal));

        if (TenderedAmount < NetTotal)
        {
            TenderedAmount = NetTotal;
        }
        else
        {
            ChangeGiven = Math.Max(0m, TenderedAmount - NetTotal);
        }
    }

    private void DeleteItem(object? parameter)
    {
        if (parameter is SalesCartItemViewModel item)
        {
            Items.Remove(item);
            RecalculateTotals();
            RequestFocusSearch?.Invoke();
        }
        else if (SelectedItem != null)
        {
            Items.Remove(SelectedItem);
            RecalculateTotals();
            RequestFocusSearch?.Invoke();
        }
    }

    public void ClearAll()
    {
        Items.Clear();
        SelectedCustomer = null;
        SelectedSaleType = SaleType.Cash;
        Reference = string.Empty;
        Remarks = string.Empty;
        InvoiceDiscountAmount = 0m;
        TenderedAmount = 0m;
        ChangeGiven = 0m;
        LookupStatusMessage = string.Empty;
        IsLookupError = false;
        IsTenderModalOpen = false;
        IsSuggestionPopupOpen = false;
        SearchSuggestions.Clear();
        RecalculateTotals();
        RequestFocusSearch?.Invoke();
    }

    private void HandleEscape()
    {
        if (IsSuggestionPopupOpen)
        {
            IsSuggestionPopupOpen = false;
            SearchSuggestions.Clear();
            RequestFocusSearch?.Invoke();
            return;
        }

        if (IsTenderModalOpen)
        {
            IsTenderModalOpen = false;
            RequestFocusSearch?.Invoke();
        }
        else
        {
            ClearAll();
        }
    }

    private void OpenTenderModal()
    {
        if (Items.Count == 0) return;

        if (SelectedSaleType == SaleType.Credit && SelectedCustomer == null)
        {
            LookupStatusMessage = "Please select a customer for Credit Sales.";
            IsLookupError = true;
            return;
        }

        TenderedAmount = NetTotal;
        ChangeGiven = 0m;
        IsTenderModalOpen = true;
    }

    public async Task QuickCashSaleAsync()
    {
        if (Items.Count == 0) return;
        SelectedSaleType = SaleType.Cash;
        TenderedAmount = NetTotal;
        ChangeGiven = 0m;
        await ExecuteSaleAsync();
    }

    public async Task CompleteSaleAsync()
    {
        if (Items.Count == 0) return;

        if (SelectedSaleType == SaleType.Credit && SelectedCustomer == null)
        {
            LookupStatusMessage = "Please select a customer for Credit Sales.";
            IsLookupError = true;
            IsTenderModalOpen = false;
            return;
        }

        if (SelectedSaleType == SaleType.Cash && TenderedAmount < NetTotal)
        {
            LookupStatusMessage = $"Tendered amount ({TenderedAmount:F2}) is less than net total ({NetTotal:F2}).";
            IsLookupError = true;
            return;
        }

        IsTenderModalOpen = false;
        await ExecuteSaleAsync();
    }

    private async Task ExecuteSaleAsync()
    {
        try
        {
            var createDto = new SaleInvoiceCreateDto
            {
                OperationId = Guid.NewGuid(),
                CustomerId = SelectedCustomer?.Id,
                SaleType = SelectedSaleType,
                Reference = string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim(),
                Remarks = string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim(),
                InvoiceDiscountAmount = InvoiceDiscountAmount,
                TenderedAmount = SelectedSaleType == SaleType.Cash ? TenderedAmount : null,
                Items = Items.Select(i => new SaleInvoiceItemCreateDto
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitSalePrice = i.UnitSalePrice,
                    LineDiscountAmount = i.LineDiscountAmount
                }).ToList()
            };

            var invoice = await _saleService.PostSaleAsync(createDto);
            LookupStatusMessage = $"Sale posted successfully! Invoice #{invoice.InvoiceNumber}";
            IsLookupError = false;
            _eventBus?.Publish(UiDataChangeType.SalePosted);
            _eventBus?.Publish(UiDataChangeType.StockChanged);

            // Printing handling
            if (PrintReceiptOnSave)
            {
                var printData = await _printDataProvider.GetPrintDataAsync(invoice.Id);
                var config = _configService.GetConfig();
                if (config.ShowPreviewBeforePrint)
                {
                    RequestShowPrintPreview?.Invoke(printData);
                }
                else
                {
                    _printService.PrintInvoice(printData);
                }
            }

            ClearAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post sale.");
            LookupStatusMessage = $"Sale failed: {ex.Message}";
            IsLookupError = true;
        }
    }

    private void ParkCurrentTicket()
    {
        if (Items.Count == 0) return;

        var ticket = new ParkedTicket
        {
            Items = Items.ToList(),
            Customer = SelectedCustomer,
            SaleType = SelectedSaleType,
            Reference = Reference,
            Remarks = Remarks,
            InvoiceDiscount = InvoiceDiscountAmount,
            ParkedAt = DateTime.Now
        };

        _parkedTickets.Add(ticket);
        ParkedStatusText = $"{_parkedTickets.Count} ticket(s) on hold";
        OnPropertyChanged(nameof(ParkedTicketCount));

        ClearAll();
        LookupStatusMessage = "Current ticket placed on hold (parked).";
        IsLookupError = false;
    }

    private void RecallParkedTicket()
    {
        if (_parkedTickets.Count == 0) return;

        // Take last parked ticket
        var ticket = _parkedTickets[^1];
        _parkedTickets.RemoveAt(_parkedTickets.Count - 1);

        ParkedStatusText = _parkedTickets.Count > 0 ? $"{_parkedTickets.Count} ticket(s) on hold" : string.Empty;
        OnPropertyChanged(nameof(ParkedTicketCount));

        Items = new ObservableCollection<SalesCartItemViewModel>(ticket.Items);
        SelectedCustomer = ticket.Customer;
        SelectedSaleType = ticket.SaleType;
        Reference = ticket.Reference;
        Remarks = ticket.Remarks;
        InvoiceDiscountAmount = ticket.InvoiceDiscount;

        RecalculateTotals();
        LookupStatusMessage = "Recalled parked ticket.";
        IsLookupError = false;
        RequestFocusSearch?.Invoke();
    }

    public async Task LoadCustomersAsync()
    {
        try
        {
            var paged = await _customerRepository.GetPagedAsync(new PaginationQuery { PageNumber = 1, PageSize = 100 });
            Customers = new ObservableCollection<CustomerDto>(paged.Items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load customers.");
        }
    }

    public async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        if (_customersDirty)
        {
            _customersDirty = false;
            await LoadCustomersAsync();
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _customersDirty = false;
        await LoadCustomersAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _busSubscription?.Dispose();
        }

        base.Dispose(disposing);
    }

    private class ParkedTicket
    {
        public List<SalesCartItemViewModel> Items { get; set; } = new();
        public CustomerDto? Customer { get; set; }
        public SaleType SaleType { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public decimal InvoiceDiscount { get; set; }
        public DateTime ParkedAt { get; set; }
    }
}

public class SalesCartItemViewModel : ViewModelBase
{
    private readonly SalesEntryViewModel _parent;
    private int _lineNumber;
    private int _productId;
    private string _productCode = string.Empty;
    private string _productName = string.Empty;
    private string _unit = string.Empty;
    private decimal _quantity = 1m;
    private decimal _unitSalePrice = 0m;
    private decimal _lineDiscountAmount = 0m;
    private decimal _availableStock = 0m;
    private string _allocationsPreview = string.Empty;

    public SalesCartItemViewModel(SalesEntryViewModel parent)
    {
        _parent = parent;
    }

    public int LineNumber
    {
        get => _lineNumber;
        set => SetProperty(ref _lineNumber, value);
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

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, Math.Max(0.0001m, value)))
            {
                Recalculate();
                _parent.RecalculateTotals();
            }
        }
    }

    public decimal UnitSalePrice
    {
        get => _unitSalePrice;
        set
        {
            if (SetProperty(ref _unitSalePrice, Math.Max(0m, value)))
            {
                Recalculate();
                _parent.RecalculateTotals();
            }
        }
    }

    public decimal LineDiscountAmount
    {
        get => _lineDiscountAmount;
        set
        {
            if (SetProperty(ref _lineDiscountAmount, Math.Max(0m, value)))
            {
                Recalculate();
                _parent.RecalculateTotals();
            }
        }
    }

    public decimal GrossAmount => Math.Round(Quantity * UnitSalePrice, 4);
    public decimal NetLineAmount => Math.Max(0m, GrossAmount - LineDiscountAmount);

    public decimal AvailableStock
    {
        get => _availableStock;
        set => SetProperty(ref _availableStock, value);
    }

    public string AllocationsPreview
    {
        get => _allocationsPreview;
        set => SetProperty(ref _allocationsPreview, value);
    }

    public void Recalculate()
    {
        OnPropertyChanged(nameof(GrossAmount));
        OnPropertyChanged(nameof(NetLineAmount));
    }
}
