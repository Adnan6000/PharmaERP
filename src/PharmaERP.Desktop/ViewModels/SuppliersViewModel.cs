using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Models;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class SuppliersViewModel : ViewModelBase
{
    private readonly ISupplierService _supplierService;
    private readonly ConnectionStateStore _connectionStore;

    private readonly ObservableCollection<SupplierDto> _items = [];
    private SupplierDto? _selectedItem;
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

    // Drawer state
    private bool _isDrawerOpen;
    private bool _isNew;
    private string _drawerTitle = "Supplier";
    private int? _formId;
    private string? _formSupplierCode;
    private string _formName = string.Empty;
    private string? _formContactPerson;
    private string? _formPhone;
    private string? _formAddress;
    private bool _formIsActive = true;
    private byte[]? _formRowVersion;
    private string? _formErrorMessage;
    private bool _isSaving;

    private CancellationTokenSource? _searchDebounceCts;

    public SuppliersViewModel(
        ISupplierService supplierService,
        ConnectionStateStore connectionStore)
    {
        _supplierService = supplierService;
        _connectionStore = connectionStore;

        Items = new ReadOnlyObservableCollection<SupplierDto>(_items);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadDataAsync(ct),
            canExecute: _ => !IsLoading && !IsSaving);

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

        NewCommand = new RelayCommand(
            execute: _ => OpenNewDrawer(),
            canExecute: _ => !IsLoading && !IsSaving);

        EditCommand = new RelayCommand(
            execute: param =>
            {
                var target = (param as SupplierDto) ?? SelectedItem;
                if (target != null) OpenEditDrawer(target);
            },
            canExecute: param => ((param as SupplierDto) ?? SelectedItem) != null && !IsLoading && !IsSaving);

        SaveCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveSupplierAsync(ct),
            canExecute: _ => IsDrawerOpen && !IsSaving);

        CancelCommand = new RelayCommand(
            execute: _ => CloseDrawer(),
            canExecute: _ => IsDrawerOpen && !IsSaving);

        ToggleActiveCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                var target = (param as SupplierDto) ?? SelectedItem;
                if (target != null) await ToggleActiveAsync(target, ct);
            },
            canExecute: param => ((param as SupplierDto) ?? SelectedItem) != null && !IsLoading);

        ClearFiltersCommand = new AsyncRelayCommand(
            execute: async (_, ct) =>
            {
                SearchTerm = string.Empty;
                PageNumber = 1;
                await LoadDataAsync(ct);
            });

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;
        _ = LoadDataAsync(CancellationToken.None);
    }

    public ReadOnlyObservableCollection<SupplierDto> Items { get; }
    public ConnectionStateStore ConnectionStore => _connectionStore;

    public SupplierDto? SelectedItem
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

    public string? FormSupplierCode
    {
        get => _formSupplierCode;
        set => SetField(ref _formSupplierCode, value);
    }

    public string FormName
    {
        get => _formName;
        set => SetField(ref _formName, value);
    }

    public string? FormContactPerson
    {
        get => _formContactPerson;
        set => SetField(ref _formContactPerson, value);
    }

    public string? FormPhone
    {
        get => _formPhone;
        set => SetField(ref _formPhone, value);
    }

    public string? FormAddress
    {
        get => _formAddress;
        set => SetField(ref _formAddress, value);
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

    public ICommand RefreshCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
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
            catch (OperationCanceledException) { }
        }, token);
    }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        if (_connectionStore.IsConnected && _connectionStore.IsDatabaseInitialized)
        {
            _ = LoadDataAsync(CancellationToken.None);
        }
    }

    public async Task LoadDataAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected)
        {
            ErrorMessage = "Database is disconnected. Suppliers cannot be loaded.";
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

            var pagedResult = await _supplierService.GetSuppliersPagedAsync(query, SearchTerm, ct);

            _items.Clear();
            foreach (var item in pagedResult.Items) _items.Add(item);

            TotalCount = pagedResult.TotalCount;
            TotalPages = pagedResult.TotalPages;
            HasPreviousPage = pagedResult.HasPreviousPage;
            HasNextPage = pagedResult.HasNextPage;

            if (TotalCount == 0)
            {
                PageInfoText = "No suppliers found";
            }
            else
            {
                int start = (PageNumber - 1) * PageSize + 1;
                int end = Math.Min(PageNumber * PageSize, TotalCount);
                PageInfoText = $"Showing {start}-{end} of {TotalCount} suppliers (Page {PageNumber} of {TotalPages})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to load suppliers: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OpenNewDrawer()
    {
        _isNew = true;
        DrawerTitle = "New Supplier";
        _formId = null;
        FormSupplierCode = string.Empty;
        FormName = string.Empty;
        FormContactPerson = null;
        FormPhone = null;
        FormAddress = null;
        FormIsActive = true;
        _formRowVersion = null;
        FormErrorMessage = null;
        IsDrawerOpen = true;
    }

    public void OpenEditDrawer(SupplierDto supplier)
    {
        _isNew = false;
        DrawerTitle = $"Edit Supplier — {supplier.Name}";
        _formId = supplier.Id;
        FormSupplierCode = supplier.SupplierCode;
        FormName = supplier.Name;
        FormContactPerson = supplier.ContactPerson;
        FormPhone = supplier.Phone;
        FormAddress = supplier.Address;
        FormIsActive = supplier.IsActive;
        _formRowVersion = supplier.RowVersion;
        FormErrorMessage = null;
        IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        IsDrawerOpen = false;
        FormErrorMessage = null;
    }

    public async Task SaveSupplierAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            FormErrorMessage = "Supplier Name is required.";
            return;
        }

        IsSaving = true;
        FormErrorMessage = null;

        var dto = new SupplierUpsertDto
        {
            Id = _formId ?? 0,
            SupplierCode = FormSupplierCode?.Trim() ?? string.Empty,
            Name = FormName.Trim(),
            ContactPerson = string.IsNullOrWhiteSpace(FormContactPerson) ? null : FormContactPerson.Trim(),
            Phone = string.IsNullOrWhiteSpace(FormPhone) ? null : FormPhone.Trim(),
            Address = string.IsNullOrWhiteSpace(FormAddress) ? null : FormAddress.Trim(),
            IsActive = FormIsActive,
            RowVersion = _formRowVersion ?? []
        };

        try
        {
            if (_isNew)
            {
                await _supplierService.CreateSupplierAsync(dto, ct);
            }
            else
            {
                await _supplierService.UpdateSupplierAsync(dto, ct);
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
            FormErrorMessage = $"Error saving supplier: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ToggleActiveAsync(SupplierDto supplier, CancellationToken ct)
    {
        try
        {
            await _supplierService.ToggleActiveAsync(supplier.Id, supplier.RowVersion, ct);
            await LoadDataAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not toggle active status: {ex.Message}";
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

