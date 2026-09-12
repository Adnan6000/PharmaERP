using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class OpeningStockViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly IInventoryService _inventoryService;
    private readonly IProductService _productService;
    private readonly ConnectionStateStore _connectionStore;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private bool _isDirty = true;

    private readonly ObservableCollection<LookupDto> _products = [];
    private LookupDto? _selectedProduct;

    private string _batchNumber = string.Empty;
    private DateTime _expiryDate = DateTime.Today.AddYears(2);
    private DateTime? _manufacturingDate = DateTime.Today.AddMonths(-1);
    private decimal _quantity = 1m;
    private decimal _unitCost = 0.00m;
    private decimal? _suggestedSalePrice;

    private decimal _totalValuation;

    private bool _isSaving;
    private string? _statusMessage;
    private bool _isSuccess;

    public OpeningStockViewModel(
        IInventoryService inventoryService,
        IProductService productService,
        ConnectionStateStore connectionStore,
        IUiDataChangeBus? eventBus = null)
    {
        _inventoryService = inventoryService;
        _productService = productService;
        _connectionStore = connectionStore;
        _eventBus = eventBus;

        Products = new ReadOnlyObservableCollection<LookupDto>(_products);

        SaveCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveOpeningStockAsync(ct),
            canExecute: _ => !IsSaving && SelectedProduct != null);

        ResetCommand = new RelayCommand(
            execute: _ => ResetForm());

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.ProductChanged or UiDataChangeType.MasterChanged or UiDataChangeType.All)
                {
                    _isDirty = true;
                }
            });
        }

        _ = LoadProductsAsync(CancellationToken.None);
    }

    public ReadOnlyObservableCollection<LookupDto> Products { get; }
    public ConnectionStateStore ConnectionStore => _connectionStore;

    public LookupDto? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetField(ref _selectedProduct, value))
            {
                OnProductSelected(value);
            }
        }
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
                CalculateValuation();
            }
        }
    }

    public decimal UnitCost
    {
        get => _unitCost;
        set
        {
            if (SetField(ref _unitCost, value))
            {
                CalculateValuation();
            }
        }
    }

    public decimal? SuggestedSalePrice
    {
        get => _suggestedSalePrice;
        set => SetField(ref _suggestedSalePrice, value);
    }

    public decimal TotalValuation
    {
        get => _totalValuation;
        private set => SetField(ref _totalValuation, value);
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

    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }

    public async Task LoadProductsAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        try
        {
            var paged = await _productService.GetProductsPagedAsync(new PaginationQuery { PageNumber = 1, PageSize = 1000 }, null, cancellationToken: ct);
            _products.Clear();
            foreach (var p in paged.Items.Where(x => x.IsActive))
            {
                _products.Add(new LookupDto(p.Id, p.Name, p.ProductCode));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load products: {ex.Message}";
            IsSuccess = false;
        }
    }

    private void OnProductSelected(LookupDto? product)
    {
        if (product == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var full = await _productService.GetProductByIdAsync(product.Id);
                if (full != null)
                {
                    UnitCost = full.DefaultPurchasePrice;
                    SuggestedSalePrice = full.DefaultSalePrice;
                }
            }
            catch { }
        });
    }

    private void CalculateValuation()
    {
        TotalValuation = Quantity * UnitCost;
    }

    public async Task SaveOpeningStockAsync(CancellationToken ct)
    {
        if (SelectedProduct == null)
        {
            StatusMessage = "Please select a product.";
            IsSuccess = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(BatchNumber))
        {
            StatusMessage = "Batch number is required.";
            IsSuccess = false;
            return;
        }

        if (Quantity <= 0)
        {
            StatusMessage = "Quantity must be greater than zero.";
            IsSuccess = false;
            return;
        }

        if (UnitCost < 0)
        {
            StatusMessage = "Unit cost cannot be negative.";
            IsSuccess = false;
            return;
        }

        var exp = DateOnly.FromDateTime(ExpiryDate);
        if (exp <= DateOnly.FromDateTime(DateTime.Today))
        {
            StatusMessage = "Expiry date must be in the future.";
            IsSuccess = false;
            return;
        }

        IsSaving = true;
        StatusMessage = null;

        var dto = new OpeningStockCreateDto
        {
            ProductId = SelectedProduct.Id,
            BatchNumber = BatchNumber.Trim(),
            ExpiryDate = exp,
            ManufacturingDate = ManufacturingDate.HasValue ? DateOnly.FromDateTime(ManufacturingDate.Value) : null,
            Quantity = Quantity,
            UnitCost = UnitCost,
            SuggestedSalePrice = SuggestedSalePrice
        };

        try
        {
            var result = await _inventoryService.RecordOpeningStockAsync(dto, ct);
            IsSuccess = true;
            StatusMessage = $"Opening stock recorded successfully for batch '{result.BatchNumber}'. Current balance: {result.QuantityOnHand}.";
            _eventBus?.Publish(UiDataChangeType.StockChanged);
            ResetForm();
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"Error recording opening stock: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void ResetForm()
    {
        BatchNumber = string.Empty;
        ExpiryDate = DateTime.Today.AddYears(2);
        ManufacturingDate = DateTime.Today.AddMonths(-1);
        Quantity = 1m;
        UnitCost = 0.00m;
        SuggestedSalePrice = null;
        TotalValuation = 0.00m;
    }

    public async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        if (_isDirty)
        {
            _isDirty = false;
            await LoadProductsAsync(ct);
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _isDirty = false;
        await LoadProductsAsync(ct);
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

