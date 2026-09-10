using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Desktop.ViewModels;

public class ChartOfAccountsViewModel : ViewModelBase
{
    private readonly IAccountService _accountService;
    private readonly ConnectionStateStore _connectionStore;

    private readonly ObservableCollection<AccountDto> _allAccounts = [];
    private readonly ObservableCollection<AccountDto> _filteredAccounts = [];
    private AccountDto? _selectedAccount;
    private string _searchTerm = string.Empty;
    private AccountType? _selectedTypeFilter;
    private bool _isLoading;
    private string? _errorMessage;

    // Add Account Drawer
    private bool _isDrawerOpen;
    private string _formAccountCode = string.Empty;
    private string _formAccountName = string.Empty;
    private AccountType _formAccountType = AccountType.Asset;
    private int? _formParentAccountId;
    private string? _formDescription;
    private bool _formAllowPosting = true;
    private string? _formErrorMessage;
    private bool _isSaving;

    public ChartOfAccountsViewModel(
        IAccountService accountService,
        ConnectionStateStore connectionStore)
    {
        _accountService = accountService;
        _connectionStore = connectionStore;

        Accounts = new ReadOnlyObservableCollection<AccountDto>(_filteredAccounts);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadAccountsAsync(ct),
            canExecute: _ => !IsLoading && !IsSaving);

        OpenNewAccountDrawerCommand = new RelayCommand(
            execute: _ => OpenNewDrawer(),
            canExecute: _ => !IsLoading && !IsSaving);

        CloseDrawerCommand = new RelayCommand(
            execute: _ => CloseDrawer());

        SaveAccountCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveAccountAsync(ct),
            canExecute: _ => !IsSaving && !string.IsNullOrWhiteSpace(FormAccountCode) && !string.IsNullOrWhiteSpace(FormAccountName));

        AccountTypes = Enum.GetValues<AccountType>();
    }

    public ReadOnlyObservableCollection<AccountDto> Accounts { get; }
    public AccountType[] AccountTypes { get; }

    public AccountDto? SelectedAccount
    {
        get => _selectedAccount;
        set => SetField(ref _selectedAccount, value);
    }

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (SetField(ref _searchTerm, value))
            {
                ApplyFilter();
            }
        }
    }

    public AccountType? SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetField(ref _selectedTypeFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public bool IsDrawerOpen
    {
        get => _isDrawerOpen;
        set => SetField(ref _isDrawerOpen, value);
    }

    public string FormAccountCode
    {
        get => _formAccountCode;
        set => SetField(ref _formAccountCode, value);
    }

    public string FormAccountName
    {
        get => _formAccountName;
        set => SetField(ref _formAccountName, value);
    }

    public AccountType FormAccountType
    {
        get => _formAccountType;
        set => SetField(ref _formAccountType, value);
    }

    public int? FormParentAccountId
    {
        get => _formParentAccountId;
        set => SetField(ref _formParentAccountId, value);
    }

    public string? FormDescription
    {
        get => _formDescription;
        set => SetField(ref _formDescription, value);
    }

    public bool FormAllowPosting
    {
        get => _formAllowPosting;
        set => SetField(ref _formAllowPosting, value);
    }

    public string? FormErrorMessage
    {
        get => _formErrorMessage;
        set => SetField(ref _formErrorMessage, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetField(ref _isSaving, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenNewAccountDrawerCommand { get; }
    public ICommand CloseDrawerCommand { get; }
    public ICommand SaveAccountCommand { get; }

    public async Task LoadAccountsAsync(CancellationToken ct = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var list = await _accountService.GetAllAccountsAsync(false, ct);
            _allAccounts.Clear();
            foreach (var acc in list.OrderBy(a => a.AccountCode))
            {
                _allAccounts.Add(acc);
            }
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Chart of Accounts: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        _filteredAccounts.Clear();
        var query = _allAccounts.AsEnumerable();

        if (SelectedTypeFilter.HasValue)
        {
            query = query.Where(a => a.AccountType == SelectedTypeFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(a =>
                a.AccountCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (a.Description != null && a.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var a in query)
        {
            _filteredAccounts.Add(a);
        }
    }

    private void OpenNewDrawer()
    {
        FormAccountCode = string.Empty;
        FormAccountName = string.Empty;
        FormAccountType = SelectedAccount?.AccountType ?? AccountType.Asset;
        FormParentAccountId = SelectedAccount?.Id;
        FormDescription = string.Empty;
        FormAllowPosting = true;
        FormErrorMessage = null;
        IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        IsDrawerOpen = false;
        FormErrorMessage = null;
    }

    private async Task SaveAccountAsync(CancellationToken ct = default)
    {
        if (IsSaving) return;
        IsSaving = true;
        FormErrorMessage = null;

        try
        {
            var dto = new AccountCreateDto
            {
                AccountCode = FormAccountCode.Trim(),
                Name = FormAccountName.Trim(),
                AccountType = FormAccountType,
                ParentAccountId = FormParentAccountId,
                AllowPosting = FormAllowPosting,
                Description = string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim()
            };

            await _accountService.CreateAccountAsync(dto, ct);
            CloseDrawer();
            await LoadAccountsAsync(ct);
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
}
