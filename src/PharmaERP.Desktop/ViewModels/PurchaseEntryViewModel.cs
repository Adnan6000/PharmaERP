using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class PurchaseEntryLineItem : ViewModelBase
{
    private int _productId;
    private string _productName = string.Empty;
    private string? _productCode;
    private string _batchNumber = string.Empty;
    private DateTime _expiryDate = DateTime.Today.AddYears(2);
    private DateTime? _manufacturingDate = DateTime.Today.AddMonths(-1);
    private decimal _quantity = 1m;
    private decimal _purchaseRate = 0.00m;
    private decimal? _suggestedSaleRate;
    private decimal _discountAmount = 0.00m;
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

    public string? ProductCode
    {
        get => _productCode;
        set => SetField(ref _productCode, value);
    }

    public string BatchNumber
    {
        get => _batchNumber;
        set => SetField(ref _batchNumber, value);
    }

    public DateTime ExpiryDate
    {
        get => _expiryDate;
        set => SetField(ref _expiryDate, value);
    }

    public DateTime? ManufacturingDate
    {
        get => _manufacturingDate;
        set => SetField(ref _manufacturingDate, value);
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

    public decimal PurchaseRate
    {
        get => _purchaseRate;
        set
        {
            if (SetField(ref _purchaseRate, value))
            {
                Recalculate();
            }
        }
    }

    public decimal? SuggestedSaleRate
    {
        get => _suggestedSaleRate;
        set => SetField(ref _suggestedSaleRate, value);
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (SetField(ref _discountAmount, value))
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
        LineTotal = Math.Max(0m, (Quantity * PurchaseRate) - DiscountAmount);
        OnLineChanged?.Invoke();
    }
}

public class PurchaseEntryViewModel : ViewModelBase
{
    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierService _supplierService;
    private readonly IProductService _productService;
    private readonly ConnectionStateStore _connectionStore;

    private readonly ObservableCollection<LookupDto> _suppliers = [];
    private readonly ObservableCollection<LookupDto> _products = [];
    private readonly ObservableCollection<PurchaseEntryLineItem> _items = [];

    private LookupDto? _selectedSupplier;
    private string? _supplierInvoiceNumber;
    private DateTime _invoiceDate = DateTime.Today;
    private DateTime? _dueDate = DateTime.Today.AddDays(30);
    private string? _remarks;

    // Line entry form buffer
    private LookupDto? _selectedProductForLine;
    private string _lineBatchNumber = string.Empty;
    private DateTime _lineExpiryDate = DateTime.Today.AddYears(2);
    private DateTime? _lineManufacturingDate = DateTime.Today.AddMonths(-1);
    private decimal _lineQuantity = 1m;
    private decimal _linePurchaseRate = 0.00m;
    private decimal? _lineSuggestedSaleRate;
    private decimal _lineDiscountAmount = 0.00m;
    private decimal _lineEstimatedTotal;

    // Totals
    private decimal _grossTotal;
    private decimal _totalDiscount;
    private decimal _netTotal;

    private bool _isSaving;
    private string? _statusMessage;
    private bool _isSuccess;

    private Action<string>? _navigateAction;

    public PurchaseEntryViewModel(
        IPurchaseService purchaseService,
        ISupplierService supplierService,
        IProductService productService,
        ConnectionStateStore connectionStore)
    {
        _purchaseService = purchaseService;
        _supplierService = supplierService;
        _productService = productService;
        _connectionStore = connectionStore;

        Suppliers = new ReadOnlyObservableCollection<LookupDto>(_suppliers);
        Products = new ReadOnlyObservableCollection<LookupDto>(_products);
        Items = new ReadOnlyObservableCollection<PurchaseEntryLineItem>(_items);

        AddLineCommand = new RelayCommand(
            execute: _ => AddCurrentLine(),
            canExecute: _ => SelectedProductForLine != null && !string.IsNullOrWhiteSpace(LineBatchNumber) && LineQuantity > 0);

        RemoveLineCommand = new RelayCommand(
            execute: param =>
            {
                if (param is PurchaseEntryLineItem item)
                {
                    _items.Remove(item);
                    RecalculateTotals();
                }
            });

        SaveInvoiceCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveAndPostInvoiceAsync(ct),
            canExecute: _ => !IsSaving && SelectedSupplier != null && _items.Count > 0);

        ClearFormCommand = new RelayCommand(
            execute: _ => ResetAll());

        _ = RefreshLookupsAsync(CancellationToken.None);
    }

    public void SetNavigateAction(Action<string> navigateAction)
    {
        _navigateAction = navigateAction;
    }

    public ConnectionStateStore ConnectionStore => _connectionStore;
    public ReadOnlyObservableCollection<LookupDto> Suppliers { get; }
    public ReadOnlyObservableCollection<LookupDto> Products { get; }
    public ReadOnlyObservableCollection<PurchaseEntryLineItem> Items { get; }

    public LookupDto? SelectedSupplier
    {
        get => _selectedSupplier;
        set => SetField(ref _selectedSupplier, value);
    }

    public string? SupplierInvoiceNumber
    {
        get => _supplierInvoiceNumber;
        set => SetField(ref _supplierInvoiceNumber, value);
    }

    public DateTime InvoiceDate
    {
        get => _invoiceDate;
        set => SetField(ref _invoiceDate, value);
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetField(ref _dueDate, value);
    }

    public string? Remarks
    {
        get => _remarks;
        set => SetField(ref _remarks, value);
    }

    // Line Entry buffer properties
    public LookupDto? SelectedProductForLine
    {
        get => _selectedProductForLine;
        set
        {
            if (SetField(ref _selectedProductForLine, value))
            {
                OnLineProductSelected(value);
            }
        }
    }

    public string LineBatchNumber
    {
        get => _lineBatchNumber;
        set => SetField(ref _lineBatchNumber, value);
    }

    public DateTime LineExpiryDate
    {
        get => _lineExpiryDate;
        set => SetField(ref _lineExpiryDate, value);
    }

    public DateTime? LineManufacturingDate
    {
        get => _lineManufacturingDate;
        set => SetField(ref _lineManufacturingDate, value);
    }

    public decimal LineQuantity
    {
        get => _lineQuantity;
        set
        {
            if (SetField(ref _lineQuantity, value))
            {
                CalculateLineBuffer();
            }
        }
    }

    public decimal LinePurchaseRate
    {
        get => _linePurchaseRate;
        set
        {
            if (SetField(ref _linePurchaseRate, value))
            {
                CalculateLineBuffer();
            }
        }
    }

    public decimal? LineSuggestedSaleRate
    {
        get => _lineSuggestedSaleRate;
        set => SetField(ref _lineSuggestedSaleRate, value);
    }

    public decimal LineDiscountAmount
    {
        get => _lineDiscountAmount;
        set
        {
            if (SetField(ref _lineDiscountAmount, value))
            {
                CalculateLineBuffer();
            }
        }
    }

    public decimal LineEstimatedTotal
    {
        get => _lineEstimatedTotal;
        private set => SetField(ref _lineEstimatedTotal, value);
    }

    // Totals
    public decimal GrossTotal
    {
        get => _grossTotal;
        private set => SetField(ref _grossTotal, value);
    }

    public decimal TotalDiscount
    {
        get => _totalDiscount;
        private set => SetField(ref _totalDiscount, value);
    }

    public decimal NetTotal
    {
        get => _netTotal;
        private set => SetField(ref _netTotal, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetField(ref _isSaving, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        private set => SetField(ref _isSuccess, value);
    }

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SaveInvoiceCommand { get; }
    public ICommand ClearFormCommand { get; }

    public async Task RefreshLookupsAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        try
        {
            var supps = await _supplierService.GetActiveLookupsAsync(ct);
            _suppliers.Clear();
            foreach (var s in supps) _suppliers.Add(s);

            var prods = await _productService.GetProductsPagedAsync(new PaginationQuery { PageNumber = 1, PageSize = 1000 }, null, ct);
            _products.Clear();
            foreach (var p in prods.Items.Where(x => x.IsActive))
            {
                _products.Add(new LookupDto(p.Id, p.Name, p.ProductCode));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed loading suppliers/products: {ex.Message}";
            IsSuccess = false;
        }
    }

    private void OnLineProductSelected(LookupDto? product)
    {
        if (product == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var full = await _productService.GetProductByIdAsync(product.Id);
                if (full != null)
                {
                    LinePurchaseRate = full.DefaultPurchasePrice;
                    LineSuggestedSaleRate = full.DefaultSalePrice;
                }
            }
            catch { }
        });
    }

    private void CalculateLineBuffer()
    {
        LineEstimatedTotal = Math.Max(0m, (LineQuantity * LinePurchaseRate) - LineDiscountAmount);
    }

    private void AddCurrentLine()
    {
        if (SelectedProductForLine == null) return;

        string batch = LineBatchNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(batch))
        {
            StatusMessage = "Batch number is required.";
            IsSuccess = false;
            return;
        }

        if (LineQuantity <= 0)
        {
            StatusMessage = "Quantity must be greater than zero.";
            IsSuccess = false;
            return;
        }

        var exp = DateOnly.FromDateTime(LineExpiryDate);
        if (exp <= DateOnly.FromDateTime(DateTime.Today))
        {
            StatusMessage = "Expiry date cannot be in the past.";
            IsSuccess = false;
            return;
        }

        var line = new PurchaseEntryLineItem
        {
            ProductId = SelectedProductForLine.Id,
            ProductName = SelectedProductForLine.Name,
            ProductCode = SelectedProductForLine.Code,
            BatchNumber = batch,
            ExpiryDate = LineExpiryDate,
            ManufacturingDate = LineManufacturingDate,
            Quantity = LineQuantity,
            PurchaseRate = LinePurchaseRate,
            SuggestedSaleRate = LineSuggestedSaleRate,
            DiscountAmount = LineDiscountAmount,
            OnLineChanged = RecalculateTotals
        };
        line.Recalculate();

        _items.Add(line);
        RecalculateTotals();

        // Reset line buffer
        SelectedProductForLine = null;
        LineBatchNumber = string.Empty;
        LineQuantity = 1m;
        LinePurchaseRate = 0.00m;
        LineSuggestedSaleRate = null;
        LineDiscountAmount = 0.00m;
        LineEstimatedTotal = 0.00m;
        StatusMessage = null;
    }

    private void RecalculateTotals()
    {
        decimal gross = 0m;
        decimal discount = 0m;
        foreach (var item in _items)
        {
            gross += item.Quantity * item.PurchaseRate;
            discount += item.DiscountAmount;
        }

        GrossTotal = gross;
        TotalDiscount = discount;
        NetTotal = Math.Max(0m, gross - discount);
    }

    public async Task SaveAndPostInvoiceAsync(CancellationToken ct)
    {
        if (SelectedSupplier == null)
        {
            StatusMessage = "Please select a supplier.";
            IsSuccess = false;
            return;
        }

        if (_items.Count == 0)
        {
            StatusMessage = "Invoice must contain at least one line item.";
            IsSuccess = false;
            return;
        }

        IsSaving = true;
        StatusMessage = null;

        var dto = new PurchaseInvoiceCreateDto
        {
            SupplierId = SelectedSupplier.Id,
            SupplierInvoiceNumber = SupplierInvoiceNumber?.Trim(),
            InvoiceDate = DateOnly.FromDateTime(InvoiceDate),
            DueDate = DueDate.HasValue ? DateOnly.FromDateTime(DueDate.Value) : null,
            Remarks = Remarks?.Trim(),
            Items = _items.Select(i => new PurchaseInvoiceItemCreateDto
            {
                ProductId = i.ProductId,
                BatchNumber = i.BatchNumber.Trim(),
                ExpiryDate = DateOnly.FromDateTime(i.ExpiryDate),
                ManufacturingDate = i.ManufacturingDate.HasValue ? DateOnly.FromDateTime(i.ManufacturingDate.Value) : null,
                Quantity = i.Quantity,
                PurchaseRate = i.PurchaseRate,
                SuggestedSaleRate = i.SuggestedSaleRate,
                DiscountAmount = i.DiscountAmount
            }).ToList()
        };

        try
        {
            var invoice = await _purchaseService.CreateAndPostPurchaseInvoiceAsync(dto, ct);
            IsSuccess = true;
            StatusMessage = $"Purchase Invoice {invoice.InvoiceNumber} successfully posted! Total: {invoice.NetTotal:C2}";
            ResetAll();
        }
        catch (DuplicateKeyException ex)
        {
            IsSuccess = false;
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"Failed to post purchase invoice: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void ResetAll()
    {
        _items.Clear();
        SupplierInvoiceNumber = null;
        InvoiceDate = DateTime.Today;
        DueDate = DateTime.Today.AddDays(30);
        Remarks = null;
        GrossTotal = 0.00m;
        TotalDiscount = 0.00m;
        NetTotal = 0.00m;

        SelectedProductForLine = null;
        LineBatchNumber = string.Empty;
        LineQuantity = 1m;
        LinePurchaseRate = 0.00m;
        LineSuggestedSaleRate = null;
        LineDiscountAmount = 0.00m;
        LineEstimatedTotal = 0.00m;
    }
}

