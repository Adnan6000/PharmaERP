using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class ProductsViewModel : ViewModelBase
{
    private readonly IProductService _productService;
    private readonly ConnectionStateStore _connectionStore;

    private readonly ObservableCollection<ProductDto> _items = [];
    private readonly ObservableCollection<LookupDto> _categories = [];
    private readonly ObservableCollection<LookupDto> _manufacturers = [];
    private readonly ObservableCollection<LookupDto> _units = [];

    private ProductDto? _selectedItem;
    private string _searchTerm = string.Empty;

    private int _pageNumber = 1;
    private int _pageSize = 20;
    private int _totalCount;
    private int _totalPages;
    private bool _hasPreviousPage;
    private bool _hasNextPage;
    private string _pageInfoText = "No items";

    private bool _isLoading;
    private string? _errorMessage;

    // Drawer / Form state
    private bool _isDrawerOpen;
    private bool _isNew;
    private string _drawerTitle = "Product Form";
    private int? _formId;
    private string _formName = string.Empty;
    private string? _formGenericName;
    private string? _formProductCode;
    private string? _formBarcode;
    private int? _formManufacturerId;
    private int? _formCategoryId;
    private int? _formUnitId;
    private decimal _formDefaultPurchasePrice;
    private decimal _formDefaultSalePrice;
    private bool _formIsActive = true;
    private byte[]? _formRowVersion;
    private string? _formErrorMessage;
    private bool _isSaving;

    private CancellationTokenSource? _searchDebounceCts;

    public ProductsViewModel(
        IProductService productService,
        ConnectionStateStore connectionStore)
    {
        _productService = productService;
        _connectionStore = connectionStore;

        Items = new ReadOnlyObservableCollection<ProductDto>(_items);
        Categories = new ReadOnlyObservableCollection<LookupDto>(_categories);
        Manufacturers = new ReadOnlyObservableCollection<LookupDto>(_manufacturers);
        Units = new ReadOnlyObservableCollection<LookupDto>(_units);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await RefreshAllAsync(ct),
            canExecute: _ => !IsLoading && !IsSaving);

        FirstPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                PageNumber = 1;
                await LoadDataAsync(ct);
            },
            canExecute: _ => HasPreviousPage && !IsLoading);

        PreviousPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                if (PageNumber > 1)
                {
                    PageNumber--;
                    await LoadDataAsync(ct);
                }
            },
            canExecute: _ => HasPreviousPage && !IsLoading);

        NextPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                if (PageNumber < TotalPages)
                {
                    PageNumber++;
                    await LoadDataAsync(ct);
                }
            },
            canExecute: _ => HasNextPage && !IsLoading);

        LastPageCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                PageNumber = TotalPages;
                await LoadDataAsync(ct);
            },
            canExecute: _ => HasNextPage && !IsLoading);

        NewProductCommand = new RelayCommand(
            execute: _ => OpenNewDrawer(),
            canExecute: _ => !IsLoading && !IsSaving);

        EditProductCommand = new RelayCommand(
            execute: param =>
            {
                var target = (param as ProductDto) ?? SelectedItem;
                if (target != null)
                {
                    OpenEditDrawer(target);
                }
            },
            canExecute: param => ((param as ProductDto) ?? SelectedItem) != null && !IsLoading && !IsSaving);

        SaveProductCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveProductAsync(ct),
            canExecute: _ => IsDrawerOpen && !IsSaving);

        CancelEditCommand = new RelayCommand(
            execute: _ => CloseDrawer(),
            canExecute: _ => IsDrawerOpen && !IsSaving);

        ToggleActiveCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                var target = (param as ProductDto) ?? SelectedItem;
                if (target != null)
                {
                    await ToggleActiveAsync(target, ct);
                }
            },
            canExecute: param => ((param as ProductDto) ?? SelectedItem) != null && !IsLoading);

        ClearFiltersCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                SearchTerm = string.Empty;
                PageNumber = 1;
                await LoadDataAsync(ct);
            });

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;

        _ = RefreshAllAsync(CancellationToken.None);
    }

    public ReadOnlyObservableCollection<ProductDto> Items { get; }
    public ReadOnlyObservableCollection<LookupDto> Categories { get; }
    public ReadOnlyObservableCollection<LookupDto> Manufacturers { get; }
    public ReadOnlyObservableCollection<LookupDto> Units { get; }

    public ConnectionStateStore ConnectionStore => _connectionStore;

    public ProductDto? SelectedItem
    {
        get => _selectedItem;
        set => SetField(ref _selectedItem, value);
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

    // Drawer / Form Properties
    public bool IsDrawerOpen
    {
        get => _isDrawerOpen;
        set => SetField(ref _isDrawerOpen, value);
    }

    public string DrawerTitle
    {
        get => _drawerTitle;
        private set => SetField(ref _drawerTitle, value);
    }

    public string FormName
    {
        get => _formName;
        set => SetField(ref _formName, value);
    }

    public string? FormGenericName
    {
        get => _formGenericName;
        set => SetField(ref _formGenericName, value);
    }

    public string? FormProductCode
    {
        get => _formProductCode;
        set => SetField(ref _formProductCode, value);
    }

    public string? FormBarcode
    {
        get => _formBarcode;
        set => SetField(ref _formBarcode, value);
    }

    public int? FormManufacturerId
    {
        get => _formManufacturerId;
        set => SetField(ref _formManufacturerId, value);
    }

    public int? FormCategoryId
    {
        get => _formCategoryId;
        set => SetField(ref _formCategoryId, value);
    }

    public int? FormUnitId
    {
        get => _formUnitId;
        set => SetField(ref _formUnitId, value);
    }

    public decimal FormDefaultPurchasePrice
    {
        get => _formDefaultPurchasePrice;
        set => SetField(ref _formDefaultPurchasePrice, value);
    }

    public decimal FormDefaultSalePrice
    {
        get => _formDefaultSalePrice;
        set => SetField(ref _formDefaultSalePrice, value);
    }

    public bool FormIsActive
    {
        get => _formIsActive;
        set => SetField(ref _formIsActive, value);
    }

    public string? FormErrorMessage
    {
        get => _formErrorMessage;
        private set => SetField(ref _formErrorMessage, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetField(ref _isSaving, value);
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand NewProductCommand { get; }
    public ICommand EditProductCommand { get; }
    public ICommand SaveProductCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand ToggleActiveCommand { get; }
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
            catch (OperationCanceledException)
            {
            }
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
        await LoadLookupsAsync(ct);
        await LoadDataAsync(ct);
    }

    public async Task LoadLookupsAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected) return;

        try
        {
            var lookups = await _productService.GetLookupsAsync(ct);

            _categories.Clear();
            foreach (var item in lookups.Categories) _categories.Add(item);

            _manufacturers.Clear();
            foreach (var item in lookups.Manufacturers) _manufacturers.Add(item);

            _units.Clear();
            foreach (var item in lookups.Units) _units.Add(item);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed loading form lookups: {ex.Message}";
        }
    }

    public async Task LoadDataAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected)
        {
            ErrorMessage = "Database is disconnected. Products cannot be loaded.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var query = new PaginationQuery
            {
                PageNumber = PageNumber,
                PageSize = PageSize
            };

            var pagedResult = await _productService.GetProductsPagedAsync(
                query: query,
                searchTerm: SearchTerm,
                cancellationToken: ct);

            _items.Clear();
            foreach (var item in pagedResult.Items)
            {
                _items.Add(item);
            }

            TotalCount = pagedResult.TotalCount;
            TotalPages = pagedResult.TotalPages;
            HasPreviousPage = pagedResult.HasPreviousPage;
            HasNextPage = pagedResult.HasNextPage;

            if (TotalCount == 0)
            {
                PageInfoText = "No products found";
            }
            else
            {
                int start = (PageNumber - 1) * PageSize + 1;
                int end = Math.Min(PageNumber * PageSize, TotalCount);
                PageInfoText = $"Showing {start}-{end} of {TotalCount} products (Page {PageNumber} of {TotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to load products: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OpenNewDrawer()
    {
        _isNew = true;
        DrawerTitle = "New Product";
        _formId = null;
        FormName = string.Empty;
        FormGenericName = null;
        FormProductCode = null;
        FormBarcode = null;
        FormManufacturerId = null;
        FormCategoryId = null;
        FormUnitId = null;
        FormDefaultPurchasePrice = 0.00m;
        FormDefaultSalePrice = 0.00m;
        FormIsActive = true;
        _formRowVersion = null;
        FormErrorMessage = null;
        IsDrawerOpen = true;
    }

    public void OpenEditDrawer(ProductDto product)
    {
        _isNew = false;
        DrawerTitle = $"Edit Product — {product.Name}";
        _formId = product.Id;
        FormName = product.Name;
        FormGenericName = product.GenericName;
        FormProductCode = product.ProductCode;
        FormBarcode = product.Barcode;
        FormManufacturerId = product.ManufacturerId;
        FormCategoryId = product.CategoryId;
        FormUnitId = product.UnitId;
        FormDefaultPurchasePrice = product.DefaultPurchasePrice;
        FormDefaultSalePrice = product.DefaultSalePrice;
        FormIsActive = product.IsActive;
        _formRowVersion = product.RowVersion;
        FormErrorMessage = null;
        IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        IsDrawerOpen = false;
        FormErrorMessage = null;
    }

    public async Task SaveProductAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            FormErrorMessage = "Product Name is required.";
            return;
        }

        if (FormDefaultPurchasePrice < 0 || FormDefaultSalePrice < 0)
        {
            FormErrorMessage = "Prices cannot be negative.";
            return;
        }

        IsSaving = true;
        FormErrorMessage = null;

        var upsertDto = new ProductUpsertDto
        {
            Id = _formId ?? 0,
            Name = FormName.Trim(),
            GenericName = string.IsNullOrWhiteSpace(FormGenericName) ? null : FormGenericName.Trim(),
            ProductCode = string.IsNullOrWhiteSpace(FormProductCode) ? null : FormProductCode.Trim(),
            Barcode = string.IsNullOrWhiteSpace(FormBarcode) ? null : FormBarcode.Trim(),
            ManufacturerId = FormManufacturerId,
            CategoryId = FormCategoryId,
            UnitId = FormUnitId,
            DefaultPurchasePrice = FormDefaultPurchasePrice,
            DefaultSalePrice = FormDefaultSalePrice,
            IsActive = FormIsActive,
            RowVersion = _formRowVersion ?? []
        };

        try
        {
            if (_isNew)
            {
                await _productService.CreateProductAsync(upsertDto, ct);
            }
            else
            {
                await _productService.UpdateProductAsync(upsertDto, ct);
            }

            CloseDrawer();
            await LoadDataAsync(ct);
        }
        catch (DuplicateKeyException ex)
        {
            FormErrorMessage = ex.Message;
        }
        catch (ConcurrencyConflictException ex)
        {
            FormErrorMessage = $"{ex.Message} Please refresh the list to see updated data.";
        }
        catch (Exception ex)
        {
            FormErrorMessage = $"Error saving product: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ToggleActiveAsync(ProductDto product, CancellationToken ct)
    {
        try
        {
            await _productService.ToggleActiveAsync(product.Id, product.RowVersion, ct);
            await LoadDataAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not change product active status: {ex.Message}";
        }
    }

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

