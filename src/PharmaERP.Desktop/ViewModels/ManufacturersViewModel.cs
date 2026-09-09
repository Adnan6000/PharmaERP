using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class ManufacturersViewModel : ViewModelBase
{
    private readonly IManufacturerService _manufacturerService;
    private readonly ConnectionStateStore _connectionStore;

    private readonly ObservableCollection<ManufacturerDto> _items = [];
    private ManufacturerDto? _selectedItem;
    private string _searchTerm = string.Empty;

    private bool _isLoading;
    private string? _errorMessage;

    // Drawer / Form state
    private bool _isDrawerOpen;
    private bool _isNew;
    private string _drawerTitle = "Manufacturer Form";
    private int? _formId;
    private string _formName = string.Empty;
    private string? _formContactPerson;
    private string? _formEmail;
    private string? _formPhone;
    private bool _formIsActive = true;
    private byte[]? _formRowVersion;
    private string? _formErrorMessage;
    private bool _isSaving;

    private CancellationTokenSource? _searchDebounceCts;

    public ManufacturersViewModel(
        IManufacturerService manufacturerService,
        ConnectionStateStore connectionStore)
    {
        _manufacturerService = manufacturerService;
        _connectionStore = connectionStore;

        Items = new ReadOnlyObservableCollection<ManufacturerDto>(_items);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadDataAsync(ct),
            canExecute: _ => !IsLoading && !IsSaving);

        NewCommand = new RelayCommand(
            execute: _ => OpenNewDrawer(),
            canExecute: _ => !IsLoading && !IsSaving);

        EditCommand = new RelayCommand(
            execute: param =>
            {
                var target = (param as ManufacturerDto) ?? SelectedItem;
                if (target != null) OpenEditDrawer(target);
            },
            canExecute: param => ((param as ManufacturerDto) ?? SelectedItem) != null && !IsLoading && !IsSaving);

        SaveCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveManufacturerAsync(ct),
            canExecute: _ => IsDrawerOpen && !IsSaving);

        CancelCommand = new RelayCommand(
            execute: _ => CloseDrawer(),
            canExecute: _ => IsDrawerOpen && !IsSaving);

        ToggleActiveCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                var target = (param as ManufacturerDto) ?? SelectedItem;
                if (target != null) await ToggleActiveAsync(target, ct);
            },
            canExecute: param => ((param as ManufacturerDto) ?? SelectedItem) != null && !IsLoading);

        _connectionStore.ConnectionStateChanged += OnConnectionStateChanged;

        _ = LoadDataAsync(CancellationToken.None);
    }

    public ReadOnlyObservableCollection<ManufacturerDto> Items { get; }
    public ConnectionStateStore ConnectionStore => _connectionStore;

    public ManufacturerDto? SelectedItem
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

    // Drawer Form
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

    public string? FormContactPerson
    {
        get => _formContactPerson;
        set => SetField(ref _formContactPerson, value);
    }

    public string? FormEmail
    {
        get => _formEmail;
        set => SetField(ref _formEmail, value);
    }

    public string? FormPhone
    {
        get => _formPhone;
        set => SetField(ref _formPhone, value);
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
    public ICommand NewCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ToggleActiveCommand { get; }

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
            _ = LoadDataAsync(CancellationToken.None);
        }
    }

    public async Task LoadDataAsync(CancellationToken ct)
    {
        if (!_connectionStore.IsConnected)
        {
            ErrorMessage = "Database is disconnected. Manufacturers cannot be loaded.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var data = await _manufacturerService.GetAllAsync(SearchTerm, ct);
            _items.Clear();
            foreach (var item in data)
            {
                _items.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to load manufacturers: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OpenNewDrawer()
    {
        _isNew = true;
        DrawerTitle = "New Manufacturer";
        _formId = null;
        FormName = string.Empty;
        FormContactPerson = null;
        FormEmail = null;
        FormPhone = null;
        FormIsActive = true;
        _formRowVersion = null;
        FormErrorMessage = null;
        IsDrawerOpen = true;
    }

    public void OpenEditDrawer(ManufacturerDto m)
    {
        _isNew = false;
        DrawerTitle = $"Edit Manufacturer — {m.Name}";
        _formId = m.Id;
        FormName = m.Name;
        FormContactPerson = m.ContactPerson;
        FormEmail = m.Email;
        FormPhone = m.Phone;
        FormIsActive = m.IsActive;
        _formRowVersion = m.RowVersion;
        FormErrorMessage = null;
        IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        IsDrawerOpen = false;
        FormErrorMessage = null;
    }

    public async Task SaveManufacturerAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            FormErrorMessage = "Manufacturer Name is required.";
            return;
        }

        IsSaving = true;
        FormErrorMessage = null;

        var upsertDto = new ManufacturerUpsertDto
        {
            Id = _formId ?? 0,
            Name = FormName.Trim(),
            ContactPerson = string.IsNullOrWhiteSpace(FormContactPerson) ? null : FormContactPerson.Trim(),
            Email = string.IsNullOrWhiteSpace(FormEmail) ? null : FormEmail.Trim(),
            Phone = string.IsNullOrWhiteSpace(FormPhone) ? null : FormPhone.Trim(),
            IsActive = FormIsActive,
            RowVersion = _formRowVersion ?? []
        };

        try
        {
            if (_isNew)
            {
                await _manufacturerService.CreateAsync(upsertDto, ct);
            }
            else
            {
                await _manufacturerService.UpdateAsync(upsertDto, ct);
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
            FormErrorMessage = $"Error saving manufacturer: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ToggleActiveAsync(ManufacturerDto item, CancellationToken ct)
    {
        try
        {
            await _manufacturerService.ToggleActiveAsync(item.Id, item.RowVersion, ct);
            await LoadDataAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not change active status: {ex.Message}";
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

