using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Desktop.ViewModels;

public class LedgersViewModel : ViewModelBase
{
    private readonly IJournalService _journalService;
    private readonly IPartyLedgerService _partyLedgerService;
    private readonly IAccountService _accountService;
    private readonly ICustomerService _customerService;
    private readonly ISupplierService _supplierService;
    private readonly ConnectionStateStore _connectionStore;

    private string _activeTab = "GeneralLedger"; // GeneralLedger, CustomerLedger, SupplierLedger, DayBook, TrialBalance, CashBook, BankBook, ProfitLoss, BalanceSheet
    private DateTime _fromDate = DateTime.Today.AddDays(-30);
    private DateTime _toDate = DateTime.Today;
    private DateTime _dayBookDate = DateTime.Today;
    private bool _isLoading;
    private string? _errorMessage;

    // Lookups
    private readonly ObservableCollection<AccountDto> _accounts = [];
    private readonly ObservableCollection<AccountDto> _cashAccounts = [];
    private readonly ObservableCollection<AccountDto> _bankAccounts = [];
    private readonly ObservableCollection<LookupDto> _customers = [];
    private readonly ObservableCollection<LookupDto> _suppliers = [];

    // General Ledger State
    private AccountDto? _selectedAccount;
    private LedgerReportDto? _generalLedgerStatement;
    private readonly ObservableCollection<LedgerReportLineDto> _glLines = [];
    private int _glPage = 1;
    private int _glPageSize = 50;

    // Customer Ledger State
    private LookupDto? _selectedCustomer;
    private LedgerReportDto? _customerLedgerStatement;
    private readonly ObservableCollection<LedgerReportLineDto> _customerLines = [];
    private int _custPage = 1;
    private int _custPageSize = 50;

    // Supplier Ledger State
    private LookupDto? _selectedSupplier;
    private LedgerReportDto? _supplierLedgerStatement;
    private readonly ObservableCollection<LedgerReportLineDto> _supplierLines = [];
    private int _supPage = 1;
    private int _supPageSize = 50;

    // Cash Book State
    private AccountDto? _selectedCashAccount;
    private LedgerReportDto? _cashBookStatement;
    private readonly ObservableCollection<LedgerReportLineDto> _cashBookLines = [];

    // Bank Book State
    private AccountDto? _selectedBankAccount;
    private LedgerReportDto? _bankBookStatement;
    private readonly ObservableCollection<LedgerReportLineDto> _bankBookLines = [];

    // Day Book State
    private DayBookReportDto? _dayBookReport;
    private readonly ObservableCollection<DayBookLineDto> _dayBookJournals = [];

    // Trial Balance State
    private TrialBalanceReportDto? _trialBalanceReport;
    private readonly ObservableCollection<TrialBalanceLineDto> _tbLines = [];

    // Profit & Loss State
    private ProfitAndLossStatementDto? _profitAndLossStatement;

    // Balance Sheet State
    private BalanceSheetStatementDto? _balanceSheetStatement;

    public LedgersViewModel(
        IJournalService journalService,
        IPartyLedgerService partyLedgerService,
        IAccountService accountService,
        ICustomerService customerService,
        ISupplierService supplierService,
        ConnectionStateStore connectionStore)
    {
        _journalService = journalService;
        _partyLedgerService = partyLedgerService;
        _accountService = accountService;
        _customerService = customerService;
        _supplierService = supplierService;
        _connectionStore = connectionStore;

        Accounts = new ReadOnlyObservableCollection<AccountDto>(_accounts);
        CashAccounts = new ReadOnlyObservableCollection<AccountDto>(_cashAccounts);
        BankAccounts = new ReadOnlyObservableCollection<AccountDto>(_bankAccounts);
        Customers = new ReadOnlyObservableCollection<LookupDto>(_customers);
        Suppliers = new ReadOnlyObservableCollection<LookupDto>(_suppliers);

        GlLines = new ReadOnlyObservableCollection<LedgerReportLineDto>(_glLines);
        CustomerLines = new ReadOnlyObservableCollection<LedgerReportLineDto>(_customerLines);
        SupplierLines = new ReadOnlyObservableCollection<LedgerReportLineDto>(_supplierLines);
        CashBookLines = new ReadOnlyObservableCollection<LedgerReportLineDto>(_cashBookLines);
        BankBookLines = new ReadOnlyObservableCollection<LedgerReportLineDto>(_bankBookLines);
        DayBookJournals = new ReadOnlyObservableCollection<DayBookLineDto>(_dayBookJournals);
        TbLines = new ReadOnlyObservableCollection<TrialBalanceLineDto>(_tbLines);

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadActiveTabDataAsync(ct),
            canExecute: _ => !IsLoading);

        SwitchTabCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                if (param is string tab)
                {
                    ActiveTab = tab;
                    await LoadActiveTabDataAsync(ct);
                }
            });

        // GL Paging
        GlFirstPageCommand = new AsyncRelayCommand(async (_, ct) => { _glPage = 1; await LoadGeneralLedgerAsync(ct); }, _ => _glPage > 1 && !IsLoading);
        GlPreviousPageCommand = new AsyncRelayCommand(async (_, ct) => { if (_glPage > 1) { _glPage--; await LoadGeneralLedgerAsync(ct); } }, _ => _glPage > 1 && !IsLoading);
        GlNextPageCommand = new AsyncRelayCommand(async (_, ct) => { if (HasMoreGlPages) { _glPage++; await LoadGeneralLedgerAsync(ct); } }, _ => HasMoreGlPages && !IsLoading);

        // Cust Paging
        CustFirstPageCommand = new AsyncRelayCommand(async (_, ct) => { _custPage = 1; await LoadCustomerLedgerAsync(ct); }, _ => _custPage > 1 && !IsLoading);
        CustPreviousPageCommand = new AsyncRelayCommand(async (_, ct) => { if (_custPage > 1) { _custPage--; await LoadCustomerLedgerAsync(ct); } }, _ => _custPage > 1 && !IsLoading);
        CustNextPageCommand = new AsyncRelayCommand(async (_, ct) => { if (HasMoreCustPages) { _custPage++; await LoadCustomerLedgerAsync(ct); } }, _ => HasMoreCustPages && !IsLoading);

        // Sup Paging
        SupFirstPageCommand = new AsyncRelayCommand(async (_, ct) => { _supPage = 1; await LoadSupplierLedgerAsync(ct); }, _ => _supPage > 1 && !IsLoading);
        SupPreviousPageCommand = new AsyncRelayCommand(async (_, ct) => { if (_supPage > 1) { _supPage--; await LoadSupplierLedgerAsync(ct); } }, _ => _supPage > 1 && !IsLoading);
        SupNextPageCommand = new AsyncRelayCommand(async (_, ct) => { if (HasMoreSupPages) { _supPage++; await LoadSupplierLedgerAsync(ct); } }, _ => HasMoreSupPages && !IsLoading);
    }

    public ReadOnlyObservableCollection<AccountDto> Accounts { get; }
    public ReadOnlyObservableCollection<AccountDto> CashAccounts { get; }
    public ReadOnlyObservableCollection<AccountDto> BankAccounts { get; }
    public ReadOnlyObservableCollection<LookupDto> Customers { get; }
    public ReadOnlyObservableCollection<LookupDto> Suppliers { get; }

    public ReadOnlyObservableCollection<LedgerReportLineDto> GlLines { get; }
    public ReadOnlyObservableCollection<LedgerReportLineDto> CustomerLines { get; }
    public ReadOnlyObservableCollection<LedgerReportLineDto> SupplierLines { get; }
    public ReadOnlyObservableCollection<LedgerReportLineDto> CashBookLines { get; }
    public ReadOnlyObservableCollection<LedgerReportLineDto> BankBookLines { get; }
    public ReadOnlyObservableCollection<DayBookLineDto> DayBookJournals { get; }
    public ReadOnlyObservableCollection<TrialBalanceLineDto> TbLines { get; }

    public string ActiveTab { get => _activeTab; set => SetField(ref _activeTab, value); }
    public DateTime FromDate { get => _fromDate; set => SetField(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => SetField(ref _toDate, value); }
    public DateTime DayBookDate { get => _dayBookDate; set => SetField(ref _dayBookDate, value); }
    public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
    public string? ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }

    public AccountDto? SelectedAccount
    {
        get => _selectedAccount;
        set { if (SetField(ref _selectedAccount, value)) { _glPage = 1; _ = LoadGeneralLedgerAsync(); } }
    }

    public AccountDto? SelectedCashAccount
    {
        get => _selectedCashAccount;
        set { if (SetField(ref _selectedCashAccount, value)) { _ = LoadCashBookAsync(); } }
    }

    public AccountDto? SelectedBankAccount
    {
        get => _selectedBankAccount;
        set { if (SetField(ref _selectedBankAccount, value)) { _ = LoadBankBookAsync(); } }
    }

    public LookupDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set { if (SetField(ref _selectedCustomer, value)) { _custPage = 1; _ = LoadCustomerLedgerAsync(); } }
    }

    public LookupDto? SelectedSupplier
    {
        get => _selectedSupplier;
        set { if (SetField(ref _selectedSupplier, value)) { _supPage = 1; _ = LoadSupplierLedgerAsync(); } }
    }

    public LedgerReportDto? GeneralLedgerStatement { get => _generalLedgerStatement; private set => SetField(ref _generalLedgerStatement, value); }
    public LedgerReportDto? CustomerLedgerStatement { get => _customerLedgerStatement; private set => SetField(ref _customerLedgerStatement, value); }
    public LedgerReportDto? SupplierLedgerStatement { get => _supplierLedgerStatement; private set => SetField(ref _supplierLedgerStatement, value); }
    public LedgerReportDto? CashBookStatement { get => _cashBookStatement; private set => SetField(ref _cashBookStatement, value); }
    public LedgerReportDto? BankBookStatement { get => _bankBookStatement; private set => SetField(ref _bankBookStatement, value); }

    public DayBookReportDto? DayBookReport { get => _dayBookReport; private set => SetField(ref _dayBookReport, value); }
    public TrialBalanceReportDto? TrialBalanceReport { get => _trialBalanceReport; private set => SetField(ref _trialBalanceReport, value); }
    public ProfitAndLossStatementDto? ProfitAndLossStatement { get => _profitAndLossStatement; private set => SetField(ref _profitAndLossStatement, value); }
    public BalanceSheetStatementDto? BalanceSheetStatement { get => _balanceSheetStatement; private set => SetField(ref _balanceSheetStatement, value); }

    public bool HasMoreGlPages => GeneralLedgerStatement != null && (_glPage * _glPageSize) < GeneralLedgerStatement.TotalRows;
    public bool HasMoreCustPages => CustomerLedgerStatement != null && (_custPage * _custPageSize) < CustomerLedgerStatement.TotalRows;
    public bool HasMoreSupPages => SupplierLedgerStatement != null && (_supPage * _supPageSize) < SupplierLedgerStatement.TotalRows;

    public ICommand RefreshCommand { get; }
    public ICommand SwitchTabCommand { get; }
    public ICommand GlFirstPageCommand { get; }
    public ICommand GlPreviousPageCommand { get; }
    public ICommand GlNextPageCommand { get; }
    public ICommand CustFirstPageCommand { get; }
    public ICommand CustPreviousPageCommand { get; }
    public ICommand CustNextPageCommand { get; }
    public ICommand SupFirstPageCommand { get; }
    public ICommand SupPreviousPageCommand { get; }
    public ICommand SupNextPageCommand { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadLookupsAsync(ct);
        await LoadActiveTabDataAsync(ct);
    }

    private async Task LoadLookupsAsync(CancellationToken ct)
    {
        try
        {
            var accounts = await _accountService.GetAllAccountsAsync(false, ct);
            _accounts.Clear();
            _cashAccounts.Clear();
            _bankAccounts.Clear();

            foreach (var a in accounts.OrderBy(x => x.AccountCode))
            {
                if (a.AllowPosting)
                {
                    _accounts.Add(a);
                    if (a.IsCashAccount || a.SystemAccountType == SystemAccountType.CashOnHand)
                    {
                        _cashAccounts.Add(a);
                    }
                    if (a.IsBankAccount || a.SystemAccountType == SystemAccountType.DefaultBankAccount)
                    {
                        _bankAccounts.Add(a);
                    }
                }
            }

            _selectedAccount = _accounts.FirstOrDefault();
            _selectedCashAccount = _cashAccounts.FirstOrDefault();
            _selectedBankAccount = _bankAccounts.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedAccount));
            OnPropertyChanged(nameof(SelectedCashAccount));
            OnPropertyChanged(nameof(SelectedBankAccount));

            var customers = await _customerService.GetActiveLookupsAsync(ct);
            _customers.Clear();
            foreach (var c in customers.OrderBy(x => x.Name)) _customers.Add(c);
            _selectedCustomer = _customers.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedCustomer));

            var suppliers = await _supplierService.GetActiveLookupsAsync(ct);
            _suppliers.Clear();
            foreach (var s in suppliers.OrderBy(x => x.Name)) _suppliers.Add(s);
            _selectedSupplier = _suppliers.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedSupplier));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load accounting lookups: {ex.Message}";
        }
    }

    public async Task LoadActiveTabDataAsync(CancellationToken ct = default)
    {
        switch (ActiveTab)
        {
            case "GeneralLedger":
                await LoadGeneralLedgerAsync(ct);
                break;
            case "CustomerLedger":
                await LoadCustomerLedgerAsync(ct);
                break;
            case "SupplierLedger":
                await LoadSupplierLedgerAsync(ct);
                break;
            case "CashBook":
                await LoadCashBookAsync(ct);
                break;
            case "BankBook":
                await LoadBankBookAsync(ct);
                break;
            case "DayBook":
                await LoadDayBookAsync(ct);
                break;
            case "TrialBalance":
                await LoadTrialBalanceAsync(ct);
                break;
            case "ProfitLoss":
                await LoadProfitLossAsync(ct);
                break;
            case "BalanceSheet":
                await LoadBalanceSheetAsync(ct);
                break;
        }
    }

    private async Task LoadGeneralLedgerAsync(CancellationToken ct = default)
    {
        if (SelectedAccount == null) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var stmt = await _journalService.GetGeneralLedgerAsync(
                SelectedAccount.Id,
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                _glPage,
                _glPageSize,
                ct);

            GeneralLedgerStatement = stmt;
            _glLines.Clear();
            foreach (var line in stmt.Lines) _glLines.Add(line);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load General Ledger: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCashBookAsync(CancellationToken ct = default)
    {
        if (SelectedCashAccount == null) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var stmt = await _journalService.GetGeneralLedgerAsync(
                SelectedCashAccount.Id,
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                1,
                100,
                ct);

            CashBookStatement = stmt;
            _cashBookLines.Clear();
            foreach (var line in stmt.Lines) _cashBookLines.Add(line);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Cash Book: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadBankBookAsync(CancellationToken ct = default)
    {
        if (SelectedBankAccount == null) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var stmt = await _journalService.GetGeneralLedgerAsync(
                SelectedBankAccount.Id,
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                1,
                100,
                ct);

            BankBookStatement = stmt;
            _bankBookLines.Clear();
            foreach (var line in stmt.Lines) _bankBookLines.Add(line);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Bank Book: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCustomerLedgerAsync(CancellationToken ct = default)
    {
        if (SelectedCustomer == null) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var stmt = await _partyLedgerService.GetCustomerLedgerAsync(
                SelectedCustomer.Id,
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                _custPage,
                _custPageSize,
                ct);

            CustomerLedgerStatement = stmt;
            _customerLines.Clear();
            foreach (var line in stmt.Lines) _customerLines.Add(line);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Customer Ledger: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSupplierLedgerAsync(CancellationToken ct = default)
    {
        if (SelectedSupplier == null) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var stmt = await _partyLedgerService.GetSupplierLedgerAsync(
                SelectedSupplier.Id,
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                _supPage,
                _supPageSize,
                ct);

            SupplierLedgerStatement = stmt;
            _supplierLines.Clear();
            foreach (var line in stmt.Lines) _supplierLines.Add(line);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Supplier Ledger: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDayBookAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var report = await _journalService.GetDayBookAsync(DateOnly.FromDateTime(DayBookDate), ct);
            DayBookReport = report;
            _dayBookJournals.Clear();
            foreach (var j in report.Entries) _dayBookJournals.Add(j);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Day Book: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadTrialBalanceAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var tb = await _journalService.GetTrialBalanceAsync(
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                ct);

            TrialBalanceReport = tb;
            _tbLines.Clear();
            foreach (var line in tb.Lines) _tbLines.Add(line);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Trial Balance: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadProfitLossAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ProfitAndLossStatement = await _journalService.GetProfitAndLossAsync(
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Profit & Loss: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadBalanceSheetAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            BalanceSheetStatement = await _journalService.GetBalanceSheetAsync(
                DateOnly.FromDateTime(ToDate),
                ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Balance Sheet: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
