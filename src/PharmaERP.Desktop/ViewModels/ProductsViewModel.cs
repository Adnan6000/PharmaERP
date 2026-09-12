using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class ProductsViewModel : ViewModelBase, IAsyncNavigable, IRefreshableViewModel
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly IManufacturerService _manufacturerService;
    private readonly IUnitService _unitService;
    private readonly ConnectionStateStore _connectionStore;
    private readonly IUiDataChangeBus? _eventBus;
    private readonly IDisposable? _busSubscription;
    private bool _isDirty = true;

    private readonly ObservableCollection<ProductDto> _items = [];
    private readonly ObservableCollection<LookupDto> _categories = [];
    private readonly ObservableCollection<LookupDto> _manufacturers = [];
    private readonly ObservableCollection<LookupDto> _units = [];

    private ProductDto? _selectedItem;
    private string _searchTerm = string.Empty;
    private int? _selectedCategoryFilter;
    private int? _selectedManufacturerFilter;

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

    // Quick-Add Master Overlay State
    private bool _isQuickAddOpen;
    private string _quickAddType = string.Empty; // Category, Manufacturer, Unit
    private string _quickAddTitle = string.Empty;
    private string _quickAddName = string.Empty;
    private string _quickAddSecondary = string.Empty;
    private string _quickAddSecondaryLabel = string.Empty;
    private string _quickAddSecondaryWatermark = string.Empty;
    private string? _quickAddErrorMessage;
    private bool _quickAddIsSaving;

    private CancellationTokenSource? _searchDebounceCts;

    public ProductsViewModel(
        IProductService productService,
        ICategoryService categoryService,
        IManufacturerService manufacturerService,
        IUnitService unitService,
        ConnectionStateStore connectionStore,
        IUiDataChangeBus? eventBus = null)
    {
        _productService = productService;
        _categoryService = categoryService;
        _manufacturerService = manufacturerService;
        _unitService = unitService;
        _connectionStore = connectionStore;
        _eventBus = eventBus;

        Items = new ReadOnlyObservableCollection<ProductDto>(_items);
        Categories = new ReadOnlyObservableCollection<LookupDto>(_categories);
        Manufacturers = new ReadOnlyObservableCollection<LookupDto>(_manufacturers);
        Units = new ReadOnlyObservableCollection<LookupDto>(_units);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await RefreshAllAsync(ct),
            canExecute: _ => !IsLoading && !IsSaving);

        if (_eventBus != null)
        {
            _busSubscription = _eventBus.Subscribe(changeType =>
            {
                if (changeType is UiDataChangeType.ProductChanged or UiDataChangeType.MasterChanged or UiDataChangeType.StockChanged or UiDataChangeType.All)
                {
                    _isDirty = true;
                }
            });
        }

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
                _selectedCategoryFilter = null;
                _selectedManufacturerFilter = null;
                OnPropertyChanged(nameof(SelectedCategoryFilter));
                OnPropertyChanged(nameof(SelectedManufacturerFilter));
                PageNumber = 1;
                await LoadDataAsync(ct);
            });

        // Quick Add Commands
        OpenQuickAddCategoryCommand = new RelayCommand(_ => OpenQuickAdd("Category"));
        OpenQuickAddManufacturerCommand = new RelayCommand(_ => OpenQuickAdd("Manufacturer"));
        OpenQuickAddUnitCommand = new RelayCommand(_ => OpenQuickAdd("Unit"));
        SaveQuickAddCommand = new AsyncRelayCommand(async (_, ct) => await SaveQuickAddAsync(ct), _ => !QuickAddIsSaving);
        CancelQuickAddCommand = new RelayCommand(_ => CloseQuickAdd());

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;

        _ = RefreshAllAsync(CancellationToken.None);
    }

    public ReadOnlyObservableCollection<ProductDto> Items { get; }
    public ReadOnlyObservableCollection<LookupDto> Categories { get; }
    public ReadOnlyObservableCollection<LookupDto> Manufacturers { get; }
    public ReadOnlyObservableCollection<LookupDto> Units { get; }

    public bool HasCategories => Categories.Count > 0;
    public bool HasManufacturers => Manufacturers.Count > 0;
    public bool HasUnits => Units.Count > 0;

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

    public int? SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (SetField(ref _selectedCategoryFilter, value))
            {
                PageNumber = 1;
                _ = LoadDataAsync(CancellationToken.None);
            }
        }
    }

    public int? SelectedManufacturerFilter
    {
        get => _selectedManufacturerFilter;
        set
        {
            if (SetField(ref _selectedManufacturerFilter, value))
            {
                PageNumber = 1;
                _ = LoadDataAsync(CancellationToken.None);
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

    // Quick Add Properties
    public bool IsQuickAddOpen
    {
        get => _isQuickAddOpen;
        set => SetField(ref _isQuickAddOpen, value);
    }

    public string QuickAddType
    {
        get => _quickAddType;
        private set => SetField(ref _quickAddType, value);
    }

    public string QuickAddTitle
    {
        get => _quickAddTitle;
        private set => SetField(ref _quickAddTitle, value);
    }

    public string QuickAddName
    {
        get => _quickAddName;
        set => SetField(ref _quickAddName, value);
    }

    public string QuickAddSecondary
    {
        get => _quickAddSecondary;
        set => SetField(ref _quickAddSecondary, value);
    }

    public string QuickAddSecondaryLabel
    {
        get => _quickAddSecondaryLabel;
        private set => SetField(ref _quickAddSecondaryLabel, value);
    }

    public string QuickAddSecondaryWatermark
    {
        get => _quickAddSecondaryWatermark;
        private set => SetField(ref _quickAddSecondaryWatermark, value);
    }

    public string? QuickAddErrorMessage
    {
        get => _quickAddErrorMessage;
        private set => SetField(ref _quickAddErrorMessage, value);
    }

    public bool QuickAddIsSaving
    {
        get => _quickAddIsSaving;
        private set => SetField(ref _quickAddIsSaving, value);
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
    public ICommand OpenQuickAddCategoryCommand { get; }
    public ICommand OpenQuickAddManufacturerCommand { get; }
    public ICommand OpenQuickAddUnitCommand { get; }
    public ICommand SaveQuickAddCommand { get; }
    public ICommand CancelQuickAddCommand { get; }

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

            OnPropertyChanged(nameof(HasCategories));
            OnPropertyChanged(nameof(HasManufacturers));
            OnPropertyChanged(nameof(HasUnits));
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
                categoryId: SelectedCategoryFilter,
                manufacturerId: SelectedManufacturerFilter,
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

        _ = LoadLookupsAsync(CancellationToken.None);
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

        _ = LoadLookupsAsync(CancellationToken.None);
    }

    public void CloseDrawer()
    {
        IsDrawerOpen = false;
        IsQuickAddOpen = false;
        FormErrorMessage = null;
    }

    public void OpenQuickAdd(string type)
    {
        QuickAddType = type;
        QuickAddName = string.Empty;
        QuickAddSecondary = string.Empty;
        QuickAddErrorMessage = null;

        switch (type)
        {
            case "Category":
                QuickAddTitle = "Quick Add Category";
                QuickAddSecondaryLabel = "Description (Optional)";
                QuickAddSecondaryWatermark = "e.g. Pain relief, antibiotics, vitamins";
                break;
            case "Manufacturer":
                QuickAddTitle = "Quick Add Manufacturer";
                QuickAddSecondaryLabel = "Contact Person (Optional)";
                QuickAddSecondaryWatermark = "e.g. Sales Manager / Representative";
                break;
            case "Unit":
                QuickAddTitle = "Quick Add Unit of Measure";
                QuickAddSecondaryLabel = "Abbreviation *";
                QuickAddSecondaryWatermark = "e.g. TAB, CAP, BTL";
                break;
        }

        IsQuickAddOpen = true;
    }

    public void CloseQuickAdd()
    {
        IsQuickAddOpen = false;
        QuickAddErrorMessage = null;
    }

    public async Task SaveQuickAddAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(QuickAddName))
        {
            QuickAddErrorMessage = "Name is required.";
            return;
        }

        QuickAddIsSaving = true;
        QuickAddErrorMessage = null;

        try
        {
            int? newId = null;
            if (QuickAddType == "Category")
            {
                var created = await _categoryService.CreateAsync(new CategoryUpsertDto
                {
                    Name = QuickAddName.Trim(),
                    Description = string.IsNullOrWhiteSpace(QuickAddSecondary) ? null : QuickAddSecondary.Trim(),
                    IsActive = true
                }, ct);
                newId = created.Id;
            }
            else if (QuickAddType == "Manufacturer")
            {
                var created = await _manufacturerService.CreateAsync(new ManufacturerUpsertDto
                {
                    Name = QuickAddName.Trim(),
                    ContactPerson = string.IsNullOrWhiteSpace(QuickAddSecondary) ? null : QuickAddSecondary.Trim(),
                    IsActive = true
                }, ct);
                newId = created.Id;
            }
            else if (QuickAddType == "Unit")
            {
                string abbr = string.IsNullOrWhiteSpace(QuickAddSecondary)
                    ? (QuickAddName.Trim().Length <= 3 ? QuickAddName.Trim().ToUpperInvariant() : QuickAddName.Trim().Substring(0, 3).ToUpperInvariant())
                    : QuickAddSecondary.Trim().ToUpperInvariant();

                var created = await _unitService.CreateAsync(new UnitUpsertDto
                {
                    Name = QuickAddName.Trim(),
                    Abbreviation = abbr,
                    IsActive = true
                }, ct);
                newId = created.Id;
            }

            // Reload lookups immediately
            await LoadLookupsAsync(ct);

            // Auto-select newly created master item
            if (newId.HasValue)
            {
                if (QuickAddType == "Category") FormCategoryId = newId.Value;
                else if (QuickAddType == "Manufacturer") FormManufacturerId = newId.Value;
                else if (QuickAddType == "Unit") FormUnitId = newId.Value;
            }

            IsQuickAddOpen = false;
        }
        catch (DuplicateKeyException ex)
        {
            QuickAddErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            QuickAddErrorMessage = $"Error saving {QuickAddType}: {ex.Message}";
        }
        finally
        {
            QuickAddIsSaving = false;
        }
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
            _eventBus?.Publish(UiDataChangeType.ProductChanged);
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
            _eventBus?.Publish(UiDataChangeType.ProductChanged);
            await LoadDataAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not change product active status: {ex.Message}";
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

