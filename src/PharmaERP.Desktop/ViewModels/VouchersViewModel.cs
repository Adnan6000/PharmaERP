using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Application.Services;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Desktop.ViewModels;

public class VouchersViewModel : ViewModelBase
{
    private readonly IVoucherService _voucherService;
    private readonly IPaymentSettlementService _settlementService;
    private readonly IAccountService _accountService;
    private readonly ICustomerService _customerService;
    private readonly ISupplierService _supplierService;
    private readonly ConnectionStateStore _connectionStore;

    // List & Filter
    private string _activeTab = "Receipt"; // Receipt, Payment, Contra, Journal, OpeningBalance
    private DateTime _fromDate = DateTime.Today.AddDays(-30);
    private DateTime _toDate = DateTime.Today;
    private bool _isLoading;
    private string? _errorMessage;

    // Collections
    private readonly ObservableCollection<ReceiptVoucherDto> _receiptVouchers = [];
    private readonly ObservableCollection<PaymentVoucherDto> _paymentVouchers = [];
    private readonly ObservableCollection<ContraVoucherDto> _contraVouchers = [];
    private readonly ObservableCollection<JournalVoucherDto> _journalVouchers = [];
    private readonly ObservableCollection<OpeningBalanceVoucherDto> _openingBalanceVouchers = [];

    private readonly ObservableCollection<AccountDto> _allAccounts = [];
    private readonly ObservableCollection<AccountDto> _cashAndBankAccounts = [];
    private readonly ObservableCollection<LookupDto> _customers = [];
    private readonly ObservableCollection<LookupDto> _suppliers = [];

    // Selected items
    private ReceiptVoucherDto? _selectedReceiptVoucher;
    private PaymentVoucherDto? _selectedPaymentVoucher;
    private ContraVoucherDto? _selectedContraVoucher;
    private JournalVoucherDto? _selectedJournalVoucher;
    private OpeningBalanceVoucherDto? _selectedOpeningBalanceVoucher;

    // Drawers
    private bool _isNewRvDrawerOpen;
    private bool _isNewPvDrawerOpen;
    private bool _isNewCvDrawerOpen;
    private bool _isNewJvDrawerOpen;
    private bool _isNewObDrawerOpen;
    private bool _isCancelDrawerOpen;
    private bool _isSettlementDrawerOpen;
    private bool _isSaving;
    private string? _drawerErrorMessage;
    private string _cancelReason = string.Empty;

    // RV Form
    private DateTime _rvDate = DateTime.Today;
    private int? _rvCashBankAccountId;
    private int? _rvOffsetAccountId;
    private int? _rvCustomerId;
    private decimal _rvAmount;
    private string? _rvReference;
    private string _rvNarration = string.Empty;

    // PV Form
    private DateTime _pvDate = DateTime.Today;
    private int? _pvCashBankAccountId;
    private int? _pvOffsetAccountId;
    private int? _pvSupplierId;
    private decimal _pvAmount;
    private string? _pvReference;
    private string _pvNarration = string.Empty;

    // CV Form
    private DateTime _cvDate = DateTime.Today;
    private int? _cvFromCashBankAccountId;
    private int? _cvToCashBankAccountId;
    private decimal _cvAmount;
    private string? _cvReference;
    private string _cvNarration = string.Empty;

    // JV & OB Forms
    private DateTime _jvDate = DateTime.Today;
    private string? _jvReference;
    private string _jvNarration = string.Empty;
    private readonly ObservableCollection<JournalLineEntryModel> _jvLines = [];

    private DateTime _obDate = DateTime.Today;
    private string? _obReference;
    private string _obNarration = string.Empty;
    private readonly ObservableCollection<JournalLineEntryModel> _obLines = [];

    // Settlement Allocation State
    private readonly ObservableCollection<OpenSaleInvoiceDto> _openSaleInvoices = [];
    private readonly ObservableCollection<OpenPurchaseInvoiceDto> _openPurchaseInvoices = [];
    private readonly ObservableCollection<ReceiptVoucherAllocationDto> _rvAllocations = [];
    private readonly ObservableCollection<PaymentVoucherAllocationDto> _pvAllocations = [];
    private OpenSaleInvoiceDto? _selectedSaleInvoice;
    private OpenPurchaseInvoiceDto? _selectedPurchaseInvoice;
    private decimal _settlementAmount;
    private string _settlementTitle = string.Empty;
    private string _allocationVoidReason = string.Empty;

    public VouchersViewModel(
        IVoucherService voucherService,
        IPaymentSettlementService settlementService,
        IAccountService accountService,
        ICustomerService customerService,
        ISupplierService supplierService,
        ConnectionStateStore connectionStore)
    {
        _voucherService = voucherService;
        _settlementService = settlementService;
        _accountService = accountService;
        _customerService = customerService;
        _supplierService = supplierService;
        _connectionStore = connectionStore;

        ReceiptVouchers = new ReadOnlyObservableCollection<ReceiptVoucherDto>(_receiptVouchers);
        PaymentVouchers = new ReadOnlyObservableCollection<PaymentVoucherDto>(_paymentVouchers);
        ContraVouchers = new ReadOnlyObservableCollection<ContraVoucherDto>(_contraVouchers);
        JournalVouchers = new ReadOnlyObservableCollection<JournalVoucherDto>(_journalVouchers);
        OpeningBalanceVouchers = new ReadOnlyObservableCollection<OpeningBalanceVoucherDto>(_openingBalanceVouchers);

        AllAccounts = new ReadOnlyObservableCollection<AccountDto>(_allAccounts);
        CashAndBankAccounts = new ReadOnlyObservableCollection<AccountDto>(_cashAndBankAccounts);
        Customers = new ReadOnlyObservableCollection<LookupDto>(_customers);
        Suppliers = new ReadOnlyObservableCollection<LookupDto>(_suppliers);

        OpenSaleInvoices = new ReadOnlyObservableCollection<OpenSaleInvoiceDto>(_openSaleInvoices);
        OpenPurchaseInvoices = new ReadOnlyObservableCollection<OpenPurchaseInvoiceDto>(_openPurchaseInvoices);
        RvAllocations = new ReadOnlyObservableCollection<ReceiptVoucherAllocationDto>(_rvAllocations);
        PvAllocations = new ReadOnlyObservableCollection<PaymentVoucherAllocationDto>(_pvAllocations);

        JvLines = _jvLines;
        ObLines = _obLines;

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadActiveTabDataAsync(ct),
            canExecute: _ => !IsLoading && !IsSaving);

        SwitchTabCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                if (param is string tab)
                {
                    ActiveTab = tab;
                    await LoadActiveTabDataAsync(ct);
                }
            });

        OpenNewDrawerCommand = new RelayCommand(_ => OpenNewDrawerForActiveTab());
        CloseDrawerCommand = new RelayCommand(_ => CloseAllDrawers());

        SaveReceiptVoucherCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveReceiptVoucherAsync(ct),
            canExecute: _ => !IsSaving && RvCashBankAccountId.HasValue && RvAmount > 0);

        SavePaymentVoucherCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SavePaymentVoucherAsync(ct),
            canExecute: _ => !IsSaving && PvCashBankAccountId.HasValue && PvAmount > 0);

        SaveContraVoucherCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveContraVoucherAsync(ct),
            canExecute: _ => !IsSaving && CvFromCashBankAccountId.HasValue && CvToCashBankAccountId.HasValue &&
                             CvFromCashBankAccountId != CvToCashBankAccountId && CvAmount > 0);

        SaveJournalVoucherCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveJournalVoucherAsync(ct),
            canExecute: _ => !IsSaving && JvTotalDebit > 0 && JvTotalDebit == JvTotalCredit);

        SaveOpeningBalanceVoucherCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveOpeningBalanceVoucherAsync(ct),
            canExecute: _ => !IsSaving && ObTotalDebit > 0 && ObTotalDebit == ObTotalCredit);

        OpenCancelModalCommand = new RelayCommand(_ => OpenCancelModal());
        ConfirmCancelCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await ConfirmCancelAsync(ct),
            canExecute: _ => !IsSaving && !string.IsNullOrWhiteSpace(CancelReason));

        OpenSettlementDrawerCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await OpenSettlementDrawerAsync(ct),
            canExecute: _ => (ActiveTab == "Receipt" && SelectedReceiptVoucher != null) ||
                             (ActiveTab == "Payment" && SelectedPaymentVoucher != null));

        SaveSettlementAllocationCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SaveSettlementAllocationAsync(ct),
            canExecute: _ => !IsSaving && SettlementAmount > 0);

        VoidReceiptAllocationCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                if (param is ReceiptVoucherAllocationDto alloc)
                    await VoidReceiptAllocationAsync(alloc, ct);
            },
            canExecute: param => !IsSaving && param is ReceiptVoucherAllocationDto a && a.Status == AllocationStatus.Active);

        VoidPaymentAllocationCommand = new AsyncRelayCommand(
            execute: async (param, ct) =>
            {
                if (param is PaymentVoucherAllocationDto alloc)
                    await VoidPaymentAllocationAsync(alloc, ct);
            },
            canExecute: param => !IsSaving && param is PaymentVoucherAllocationDto a && a.Status == AllocationStatus.Active);

        AddJvLineCommand = new RelayCommand(_ => AddJvLine());
        RemoveJvLineCommand = new RelayCommand(param =>
        {
            if (param is JournalLineEntryModel line) _jvLines.Remove(line);
            OnPropertyChanged(nameof(JvTotalDebit));
            OnPropertyChanged(nameof(JvTotalCredit));
            OnPropertyChanged(nameof(JvDifference));
        });

        AddObLineCommand = new RelayCommand(_ => AddObLine());
        RemoveObLineCommand = new RelayCommand(param =>
        {
            if (param is JournalLineEntryModel line) _obLines.Remove(line);
            OnPropertyChanged(nameof(ObTotalDebit));
            OnPropertyChanged(nameof(ObTotalCredit));
            OnPropertyChanged(nameof(ObDifference));
        });
    }

    public ReadOnlyObservableCollection<ReceiptVoucherDto> ReceiptVouchers { get; }
    public ReadOnlyObservableCollection<PaymentVoucherDto> PaymentVouchers { get; }
    public ReadOnlyObservableCollection<ContraVoucherDto> ContraVouchers { get; }
    public ReadOnlyObservableCollection<JournalVoucherDto> JournalVouchers { get; }
    public ReadOnlyObservableCollection<OpeningBalanceVoucherDto> OpeningBalanceVouchers { get; }

    public ReadOnlyObservableCollection<AccountDto> AllAccounts { get; }
    public ReadOnlyObservableCollection<AccountDto> CashAndBankAccounts { get; }
    public ReadOnlyObservableCollection<LookupDto> Customers { get; }
    public ReadOnlyObservableCollection<LookupDto> Suppliers { get; }

    public ReadOnlyObservableCollection<OpenSaleInvoiceDto> OpenSaleInvoices { get; }
    public ReadOnlyObservableCollection<OpenPurchaseInvoiceDto> OpenPurchaseInvoices { get; }
    public ReadOnlyObservableCollection<ReceiptVoucherAllocationDto> RvAllocations { get; }
    public ReadOnlyObservableCollection<PaymentVoucherAllocationDto> PvAllocations { get; }

    public ObservableCollection<JournalLineEntryModel> JvLines { get; }
    public ObservableCollection<JournalLineEntryModel> ObLines { get; }

    public string ActiveTab { get => _activeTab; set => SetField(ref _activeTab, value); }
    public DateTime FromDate { get => _fromDate; set => SetField(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => SetField(ref _toDate, value); }
    public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
    public string? ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }

    public ReceiptVoucherDto? SelectedReceiptVoucher { get => _selectedReceiptVoucher; set => SetField(ref _selectedReceiptVoucher, value); }
    public PaymentVoucherDto? SelectedPaymentVoucher { get => _selectedPaymentVoucher; set => SetField(ref _selectedPaymentVoucher, value); }
    public ContraVoucherDto? SelectedContraVoucher { get => _selectedContraVoucher; set => SetField(ref _selectedContraVoucher, value); }
    public JournalVoucherDto? SelectedJournalVoucher { get => _selectedJournalVoucher; set => SetField(ref _selectedJournalVoucher, value); }
    public OpeningBalanceVoucherDto? SelectedOpeningBalanceVoucher { get => _selectedOpeningBalanceVoucher; set => SetField(ref _selectedOpeningBalanceVoucher, value); }

    public bool IsNewRvDrawerOpen { get => _isNewRvDrawerOpen; set => SetField(ref _isNewRvDrawerOpen, value); }
    public bool IsNewPvDrawerOpen { get => _isNewPvDrawerOpen; set => SetField(ref _isNewPvDrawerOpen, value); }
    public bool IsNewCvDrawerOpen { get => _isNewCvDrawerOpen; set => SetField(ref _isNewCvDrawerOpen, value); }
    public bool IsNewJvDrawerOpen { get => _isNewJvDrawerOpen; set => SetField(ref _isNewJvDrawerOpen, value); }
    public bool IsNewObDrawerOpen { get => _isNewObDrawerOpen; set => SetField(ref _isNewObDrawerOpen, value); }
    public bool IsCancelDrawerOpen { get => _isCancelDrawerOpen; set => SetField(ref _isCancelDrawerOpen, value); }
    public bool IsSettlementDrawerOpen { get => _isSettlementDrawerOpen; set => SetField(ref _isSettlementDrawerOpen, value); }

    public bool IsSaving { get => _isSaving; set => SetField(ref _isSaving, value); }
    public string? DrawerErrorMessage { get => _drawerErrorMessage; set => SetField(ref _drawerErrorMessage, value); }
    public string CancelReason { get => _cancelReason; set => SetField(ref _cancelReason, value); }

    // RV Props
    public DateTime RvDate { get => _rvDate; set => SetField(ref _rvDate, value); }
    public int? RvCashBankAccountId { get => _rvCashBankAccountId; set => SetField(ref _rvCashBankAccountId, value); }
    public int? RvOffsetAccountId { get => _rvOffsetAccountId; set => SetField(ref _rvOffsetAccountId, value); }
    public int? RvCustomerId { get => _rvCustomerId; set => SetField(ref _rvCustomerId, value); }
    public decimal RvAmount { get => _rvAmount; set => SetField(ref _rvAmount, value); }
    public string? RvReference { get => _rvReference; set => SetField(ref _rvReference, value); }
    public string RvNarration { get => _rvNarration; set => SetField(ref _rvNarration, value); }

    // PV Props
    public DateTime PvDate { get => _pvDate; set => SetField(ref _pvDate, value); }
    public int? PvCashBankAccountId { get => _pvCashBankAccountId; set => SetField(ref _pvCashBankAccountId, value); }
    public int? PvOffsetAccountId { get => _pvOffsetAccountId; set => SetField(ref _pvOffsetAccountId, value); }
    public int? PvSupplierId { get => _pvSupplierId; set => SetField(ref _pvSupplierId, value); }
    public decimal PvAmount { get => _pvAmount; set => SetField(ref _pvAmount, value); }
    public string? PvReference { get => _pvReference; set => SetField(ref _pvReference, value); }
    public string PvNarration { get => _pvNarration; set => SetField(ref _pvNarration, value); }

    // CV Props
    public DateTime CvDate { get => _cvDate; set => SetField(ref _cvDate, value); }
    public int? CvFromCashBankAccountId { get => _cvFromCashBankAccountId; set => SetField(ref _cvFromCashBankAccountId, value); }
    public int? CvToCashBankAccountId { get => _cvToCashBankAccountId; set => SetField(ref _cvToCashBankAccountId, value); }
    public decimal CvAmount { get => _cvAmount; set => SetField(ref _cvAmount, value); }
    public string? CvReference { get => _cvReference; set => SetField(ref _cvReference, value); }
    public string CvNarration { get => _cvNarration; set => SetField(ref _cvNarration, value); }

    // JV & OB Props
    public DateTime JvDate { get => _jvDate; set => SetField(ref _jvDate, value); }
    public string? JvReference { get => _jvReference; set => SetField(ref _jvReference, value); }
    public string JvNarration { get => _jvNarration; set => SetField(ref _jvNarration, value); }
    public decimal JvTotalDebit => _jvLines.Sum(l => l.Debit);
    public decimal JvTotalCredit => _jvLines.Sum(l => l.Credit);
    public decimal JvDifference => Math.Abs(JvTotalDebit - JvTotalCredit);

    public DateTime ObDate { get => _obDate; set => SetField(ref _obDate, value); }
    public string? ObReference { get => _obReference; set => SetField(ref _obReference, value); }
    public string ObNarration { get => _obNarration; set => SetField(ref _obNarration, value); }
    public decimal ObTotalDebit => _obLines.Sum(l => l.Debit);
    public decimal ObTotalCredit => _obLines.Sum(l => l.Credit);
    public decimal ObDifference => Math.Abs(ObTotalDebit - ObTotalCredit);

    // Settlement Props
    public OpenSaleInvoiceDto? SelectedSaleInvoice
    {
        get => _selectedSaleInvoice;
        set
        {
            if (SetField(ref _selectedSaleInvoice, value) && value != null)
            {
                SettlementAmount = value.OutstandingBalance;
            }
        }
    }

    public OpenPurchaseInvoiceDto? SelectedPurchaseInvoice
    {
        get => _selectedPurchaseInvoice;
        set
        {
            if (SetField(ref _selectedPurchaseInvoice, value) && value != null)
            {
                SettlementAmount = value.OutstandingBalance;
            }
        }
    }

    public decimal SettlementAmount { get => _settlementAmount; set => SetField(ref _settlementAmount, value); }
    public string SettlementTitle { get => _settlementTitle; set => SetField(ref _settlementTitle, value); }
    public string AllocationVoidReason { get => _allocationVoidReason; set => SetField(ref _allocationVoidReason, value); }

    public ICommand RefreshCommand { get; }
    public ICommand SwitchTabCommand { get; }
    public ICommand OpenNewDrawerCommand { get; }
    public ICommand CloseDrawerCommand { get; }
    public ICommand SaveReceiptVoucherCommand { get; }
    public ICommand SavePaymentVoucherCommand { get; }
    public ICommand SaveContraVoucherCommand { get; }
    public ICommand SaveJournalVoucherCommand { get; }
    public ICommand SaveOpeningBalanceVoucherCommand { get; }
    public ICommand OpenCancelModalCommand { get; }
    public ICommand ConfirmCancelCommand { get; }
    public ICommand OpenSettlementDrawerCommand { get; }
    public ICommand SaveSettlementAllocationCommand { get; }
    public ICommand VoidReceiptAllocationCommand { get; }
    public ICommand VoidPaymentAllocationCommand { get; }
    public ICommand AddJvLineCommand { get; }
    public ICommand RemoveJvLineCommand { get; }
    public ICommand AddObLineCommand { get; }
    public ICommand RemoveObLineCommand { get; }

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
            _allAccounts.Clear();
            _cashAndBankAccounts.Clear();
            foreach (var a in accounts.OrderBy(x => x.AccountCode))
            {
                if (a.AllowPosting)
                {
                    _allAccounts.Add(a);
                    if (a.IsCashAccount || a.IsBankAccount ||
                        a.SystemAccountType == SystemAccountType.CashOnHand ||
                        a.SystemAccountType == SystemAccountType.DefaultBankAccount)
                    {
                        _cashAndBankAccounts.Add(a);
                    }
                }
            }

            var custResult = await _customerService.GetActiveLookupsAsync(ct);
            _customers.Clear();
            foreach (var c in custResult.OrderBy(x => x.Name)) _customers.Add(c);

            var supResult = await _supplierService.GetActiveLookupsAsync(ct);
            _suppliers.Clear();
            foreach (var s in supResult.OrderBy(x => x.Name)) _suppliers.Add(s);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load accounting lookups: {ex.Message}";
        }
    }

    public async Task LoadActiveTabDataAsync(CancellationToken ct = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var fromDate = DateOnly.FromDateTime(FromDate);
            var toDate = DateOnly.FromDateTime(ToDate);

            switch (ActiveTab)
            {
                case "Receipt":
                    var rvs = await _voucherService.GetReceiptVouchersAsync(fromDate, toDate, ct);
                    _receiptVouchers.Clear();
                    foreach (var v in rvs) _receiptVouchers.Add(v);
                    SelectedReceiptVoucher = _receiptVouchers.FirstOrDefault();
                    break;
                case "Payment":
                    var pvs = await _voucherService.GetPaymentVouchersAsync(fromDate, toDate, ct);
                    _paymentVouchers.Clear();
                    foreach (var v in pvs) _paymentVouchers.Add(v);
                    SelectedPaymentVoucher = _paymentVouchers.FirstOrDefault();
                    break;
                case "Contra":
                    var cvs = await _voucherService.GetContraVouchersAsync(fromDate, toDate, ct);
                    _contraVouchers.Clear();
                    foreach (var v in cvs) _contraVouchers.Add(v);
                    SelectedContraVoucher = _contraVouchers.FirstOrDefault();
                    break;
                case "Journal":
                    var jvs = await _voucherService.GetJournalVouchersAsync(fromDate, toDate, ct);
                    _journalVouchers.Clear();
                    foreach (var v in jvs) _journalVouchers.Add(v);
                    SelectedJournalVoucher = _journalVouchers.FirstOrDefault();
                    break;
                case "OpeningBalance":
                    var obs = await _voucherService.GetOpeningBalanceVouchersAsync(fromDate, toDate, ct);
                    _openingBalanceVouchers.Clear();
                    foreach (var v in obs) _openingBalanceVouchers.Add(v);
                    SelectedOpeningBalanceVoucher = _openingBalanceVouchers.FirstOrDefault();
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load vouchers: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenNewDrawerForActiveTab()
    {
        DrawerErrorMessage = null;
        var arAccount = _allAccounts.FirstOrDefault(a => a.SystemAccountType == SystemAccountType.AccountsReceivableControl);
        var apAccount = _allAccounts.FirstOrDefault(a => a.SystemAccountType == SystemAccountType.AccountsPayableControl);

        switch (ActiveTab)
        {
            case "Receipt":
                RvDate = DateTime.Today;
                RvAmount = 0;
                RvReference = string.Empty;
                RvNarration = string.Empty;
                RvCashBankAccountId = _cashAndBankAccounts.FirstOrDefault()?.Id;
                RvOffsetAccountId = arAccount?.Id;
                RvCustomerId = _customers.FirstOrDefault()?.Id;
                IsNewRvDrawerOpen = true;
                break;
            case "Payment":
                PvDate = DateTime.Today;
                PvAmount = 0;
                PvReference = string.Empty;
                PvNarration = string.Empty;
                PvCashBankAccountId = _cashAndBankAccounts.FirstOrDefault()?.Id;
                PvOffsetAccountId = apAccount?.Id;
                PvSupplierId = _suppliers.FirstOrDefault()?.Id;
                IsNewPvDrawerOpen = true;
                break;
            case "Contra":
                CvDate = DateTime.Today;
                CvAmount = 0;
                CvReference = string.Empty;
                CvNarration = string.Empty;
                CvFromCashBankAccountId = _cashAndBankAccounts.FirstOrDefault()?.Id;
                CvToCashBankAccountId = _cashAndBankAccounts.Skip(1).FirstOrDefault()?.Id ?? _cashAndBankAccounts.FirstOrDefault()?.Id;
                IsNewCvDrawerOpen = true;
                break;
            case "Journal":
                JvDate = DateTime.Today;
                JvReference = string.Empty;
                JvNarration = string.Empty;
                _jvLines.Clear();
                AddJvLine();
                AddJvLine();
                IsNewJvDrawerOpen = true;
                break;
            case "OpeningBalance":
                ObDate = DateTime.Today;
                ObReference = string.Empty;
                ObNarration = "Initial Opening Balances";
                _obLines.Clear();
                AddObLine();
                AddObLine();
                IsNewObDrawerOpen = true;
                break;
        }
    }

    public void CloseAllDrawers()
    {
        IsNewRvDrawerOpen = false;
        IsNewPvDrawerOpen = false;
        IsNewCvDrawerOpen = false;
        IsNewJvDrawerOpen = false;
        IsNewObDrawerOpen = false;
        IsCancelDrawerOpen = false;
        IsSettlementDrawerOpen = false;
        DrawerErrorMessage = null;
    }

    private void AddJvLine()
    {
        var line = new JournalLineEntryModel { AccountId = _allAccounts.FirstOrDefault()?.Id ?? 0 };
        line.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(JvTotalDebit));
            OnPropertyChanged(nameof(JvTotalCredit));
            OnPropertyChanged(nameof(JvDifference));
        };
        _jvLines.Add(line);
    }

    private void AddObLine()
    {
        var line = new JournalLineEntryModel { AccountId = _allAccounts.FirstOrDefault()?.Id ?? 0 };
        line.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ObTotalDebit));
            OnPropertyChanged(nameof(ObTotalCredit));
            OnPropertyChanged(nameof(ObDifference));
        };
        _obLines.Add(line);
    }

    private async Task SaveReceiptVoucherAsync(CancellationToken ct)
    {
        if (IsSaving || !RvCashBankAccountId.HasValue || RvAmount <= 0) return;
        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var offsetAccId = RvOffsetAccountId ?? _allAccounts.First(a => a.SystemAccountType == SystemAccountType.AccountsReceivableControl).Id;
            var dto = new ReceiptVoucherCreateDto
            {
                BusinessDate = DateOnly.FromDateTime(RvDate),
                CashOrBankAccountId = RvCashBankAccountId.Value,
                OffsetAccountId = offsetAccId,
                CustomerId = RvCustomerId,
                Amount = RvAmount,
                Reference = string.IsNullOrWhiteSpace(RvReference) ? null : RvReference.Trim(),
                Narration = RvNarration?.Trim() ?? string.Empty
            };

            var created = await _voucherService.CreateReceiptVoucherAsync(dto, ct);
            _receiptVouchers.Insert(0, created);
            SelectedReceiptVoucher = created;
            CloseAllDrawers();
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SavePaymentVoucherAsync(CancellationToken ct)
    {
        if (IsSaving || !PvCashBankAccountId.HasValue || PvAmount <= 0) return;
        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var offsetAccId = PvOffsetAccountId ?? _allAccounts.First(a => a.SystemAccountType == SystemAccountType.AccountsPayableControl).Id;
            var dto = new PaymentVoucherCreateDto
            {
                BusinessDate = DateOnly.FromDateTime(PvDate),
                CashOrBankAccountId = PvCashBankAccountId.Value,
                OffsetAccountId = offsetAccId,
                SupplierId = PvSupplierId,
                Amount = PvAmount,
                Reference = string.IsNullOrWhiteSpace(PvReference) ? null : PvReference.Trim(),
                Narration = PvNarration?.Trim() ?? string.Empty
            };

            var created = await _voucherService.CreatePaymentVoucherAsync(dto, ct);
            _paymentVouchers.Insert(0, created);
            SelectedPaymentVoucher = created;
            CloseAllDrawers();
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveContraVoucherAsync(CancellationToken ct)
    {
        if (IsSaving || !CvFromCashBankAccountId.HasValue || !CvToCashBankAccountId.HasValue || CvAmount <= 0) return;
        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var dto = new ContraVoucherCreateDto
            {
                BusinessDate = DateOnly.FromDateTime(CvDate),
                SourceAccountId = CvFromCashBankAccountId.Value,
                DestinationAccountId = CvToCashBankAccountId.Value,
                Amount = CvAmount,
                Reference = string.IsNullOrWhiteSpace(CvReference) ? null : CvReference.Trim(),
                Narration = CvNarration?.Trim() ?? string.Empty
            };

            var created = await _voucherService.CreateContraVoucherAsync(dto, ct);
            _contraVouchers.Insert(0, created);
            SelectedContraVoucher = created;
            CloseAllDrawers();
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveJournalVoucherAsync(CancellationToken ct)
    {
        if (IsSaving || JvDifference != 0 || JvTotalDebit <= 0) return;
        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var lineDtos = _jvLines.Select(l => new JournalVoucherLineCreateDto
            {
                AccountId = l.AccountId,
                DebitAmount = l.Debit,
                CreditAmount = l.Credit,
                CustomerId = l.CustomerId,
                SupplierId = l.SupplierId,
                Narration = l.Description
            }).ToList();

            var dto = new JournalVoucherCreateDto
            {
                BusinessDate = DateOnly.FromDateTime(JvDate),
                Reference = string.IsNullOrWhiteSpace(JvReference) ? null : JvReference.Trim(),
                Narration = JvNarration?.Trim() ?? string.Empty,
                Lines = lineDtos
            };

            var created = await _voucherService.CreateJournalVoucherAsync(dto, ct);
            _journalVouchers.Insert(0, created);
            SelectedJournalVoucher = created;
            CloseAllDrawers();
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveOpeningBalanceVoucherAsync(CancellationToken ct)
    {
        if (IsSaving || ObDifference != 0 || ObTotalDebit <= 0) return;
        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var lineDtos = _obLines.Select(l => new OpeningBalanceLineCreateDto
            {
                AccountId = l.AccountId,
                DebitAmount = l.Debit,
                CreditAmount = l.Credit,
                CustomerId = l.CustomerId,
                SupplierId = l.SupplierId,
                Narration = l.Description
            }).ToList();

            var dto = new OpeningBalanceVoucherCreateDto
            {
                BusinessDate = DateOnly.FromDateTime(ObDate),
                Reference = string.IsNullOrWhiteSpace(ObReference) ? null : ObReference.Trim(),
                Narration = ObNarration?.Trim() ?? string.Empty,
                Lines = lineDtos
            };

            var created = await _voucherService.CreateOpeningBalanceVoucherAsync(dto, ct);
            _openingBalanceVouchers.Insert(0, created);
            SelectedOpeningBalanceVoucher = created;
            CloseAllDrawers();
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void OpenCancelModal()
    {
        CancelReason = string.Empty;
        DrawerErrorMessage = null;
        IsCancelDrawerOpen = true;
    }

    private async Task ConfirmCancelAsync(CancellationToken ct)
    {
        if (IsSaving || string.IsNullOrWhiteSpace(CancelReason)) return;
        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var reason = CancelReason.Trim();
            switch (ActiveTab)
            {
                case "Receipt" when SelectedReceiptVoucher != null:
                    var rCancelled = await _voucherService.CancelReceiptVoucherAsync(SelectedReceiptVoucher.Id, reason, ct);
                    var rIdx = _receiptVouchers.IndexOf(SelectedReceiptVoucher);
                    if (rIdx >= 0) _receiptVouchers[rIdx] = rCancelled;
                    break;
                case "Payment" when SelectedPaymentVoucher != null:
                    var pCancelled = await _voucherService.CancelPaymentVoucherAsync(SelectedPaymentVoucher.Id, reason, ct);
                    var pIdx = _paymentVouchers.IndexOf(SelectedPaymentVoucher);
                    if (pIdx >= 0) _paymentVouchers[pIdx] = pCancelled;
                    break;
                case "Contra" when SelectedContraVoucher != null:
                    var cCancelled = await _voucherService.CancelContraVoucherAsync(SelectedContraVoucher.Id, reason, ct);
                    var cIdx = _contraVouchers.IndexOf(SelectedContraVoucher);
                    if (cIdx >= 0) _contraVouchers[cIdx] = cCancelled;
                    break;
                case "Journal" when SelectedJournalVoucher != null:
                    var jCancelled = await _voucherService.CancelJournalVoucherAsync(SelectedJournalVoucher.Id, reason, ct);
                    var jIdx = _journalVouchers.IndexOf(SelectedJournalVoucher);
                    if (jIdx >= 0) _journalVouchers[jIdx] = jCancelled;
                    break;
                case "OpeningBalance" when SelectedOpeningBalanceVoucher != null:
                    var oCancelled = await _voucherService.CancelOpeningBalanceVoucherAsync(SelectedOpeningBalanceVoucher.Id, reason, ct);
                    var oIdx = _openingBalanceVouchers.IndexOf(SelectedOpeningBalanceVoucher);
                    if (oIdx >= 0) _openingBalanceVouchers[oIdx] = oCancelled;
                    break;
            }

            CloseAllDrawers();
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task OpenSettlementDrawerAsync(CancellationToken ct)
    {
        DrawerErrorMessage = null;
        _openSaleInvoices.Clear();
        _openPurchaseInvoices.Clear();
        _rvAllocations.Clear();
        _pvAllocations.Clear();
        SettlementAmount = 0;
        AllocationVoidReason = string.Empty;

        if (ActiveTab == "Receipt" && SelectedReceiptVoucher != null)

        {
            if (!SelectedReceiptVoucher.CustomerId.HasValue)
            {
                ErrorMessage = "Selected Receipt Voucher is not linked to a customer.";
                return;
            }

            SettlementTitle = $"Allocate Receipt: {SelectedReceiptVoucher.VoucherNumber} (Customer: {SelectedReceiptVoucher.CustomerName})";
            var invoices = await _settlementService.GetCustomerOpenInvoicesAsync(SelectedReceiptVoucher.CustomerId.Value, ct);
            foreach (var inv in invoices) _openSaleInvoices.Add(inv);

            var allocs = await _settlementService.GetReceiptVoucherAllocationsAsync(SelectedReceiptVoucher.Id, ct);
            foreach (var a in allocs) _rvAllocations.Add(a);

            SelectedSaleInvoice = _openSaleInvoices.FirstOrDefault();
            IsSettlementDrawerOpen = true;
        }
        else if (ActiveTab == "Payment" && SelectedPaymentVoucher != null)
        {
            if (!SelectedPaymentVoucher.SupplierId.HasValue)
            {
                ErrorMessage = "Selected Payment Voucher is not linked to a supplier.";
                return;
            }

            SettlementTitle = $"Allocate Payment: {SelectedPaymentVoucher.VoucherNumber} (Supplier: {SelectedPaymentVoucher.SupplierName})";
            var invoices = await _settlementService.GetSupplierOpenInvoicesAsync(SelectedPaymentVoucher.SupplierId.Value, ct);
            foreach (var inv in invoices) _openPurchaseInvoices.Add(inv);

            var allocs = await _settlementService.GetPaymentVoucherAllocationsAsync(SelectedPaymentVoucher.Id, ct);
            foreach (var a in allocs) _pvAllocations.Add(a);

            SelectedPurchaseInvoice = _openPurchaseInvoices.FirstOrDefault();
            IsSettlementDrawerOpen = true;
        }
    }

    private async Task SaveSettlementAllocationAsync(CancellationToken ct)
    {
        if (IsSaving || SettlementAmount <= 0) return;
        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            if (ActiveTab == "Receipt" && SelectedReceiptVoucher != null && SelectedSaleInvoice != null)
            {
                var alloc = await _settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
                {
                    ReceiptVoucherId = SelectedReceiptVoucher.Id,
                    SaleInvoiceId = SelectedSaleInvoice.InvoiceId,
                    AllocatedAmount = SettlementAmount
                }, ct);

                _rvAllocations.Insert(0, alloc);

                // Reload open invoices
                var invoices = await _settlementService.GetCustomerOpenInvoicesAsync(SelectedReceiptVoucher.CustomerId!.Value, ct);
                _openSaleInvoices.Clear();
                foreach (var inv in invoices) _openSaleInvoices.Add(inv);
                SelectedSaleInvoice = _openSaleInvoices.FirstOrDefault();
            }
            else if (ActiveTab == "Payment" && SelectedPaymentVoucher != null && SelectedPurchaseInvoice != null)
            {
                var alloc = await _settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
                {
                    PaymentVoucherId = SelectedPaymentVoucher.Id,
                    PurchaseInvoiceId = SelectedPurchaseInvoice.InvoiceId,
                    AllocatedAmount = SettlementAmount
                }, ct);

                _pvAllocations.Insert(0, alloc);

                // Reload open invoices
                var invoices = await _settlementService.GetSupplierOpenInvoicesAsync(SelectedPaymentVoucher.SupplierId!.Value, ct);
                _openPurchaseInvoices.Clear();
                foreach (var inv in invoices) _openPurchaseInvoices.Add(inv);
                SelectedPurchaseInvoice = _openPurchaseInvoices.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task VoidReceiptAllocationAsync(ReceiptVoucherAllocationDto alloc, CancellationToken ct)
    {
        if (IsSaving || alloc == null) return;
        if (string.IsNullOrWhiteSpace(AllocationVoidReason))
        {
            DrawerErrorMessage = "Please enter a void reason before voiding this allocation.";
            return;
        }

        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var voided = await _settlementService.VoidReceiptAllocationAsync(alloc.Id, AllocationVoidReason.Trim(), ct);
            AllocationVoidReason = string.Empty;

            // Refresh allocations list
            var index = _rvAllocations.IndexOf(alloc);
            if (index >= 0)
            {
                _rvAllocations[index] = voided;
            }

            // Reload open invoices
            if (SelectedReceiptVoucher?.CustomerId != null)
            {
                var invoices = await _settlementService.GetCustomerOpenInvoicesAsync(SelectedReceiptVoucher.CustomerId.Value, ct);
                _openSaleInvoices.Clear();
                foreach (var inv in invoices) _openSaleInvoices.Add(inv);
                SelectedSaleInvoice = _openSaleInvoices.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task VoidPaymentAllocationAsync(PaymentVoucherAllocationDto alloc, CancellationToken ct)
    {
        if (IsSaving || alloc == null) return;
        if (string.IsNullOrWhiteSpace(AllocationVoidReason))
        {
            DrawerErrorMessage = "Please enter a void reason before voiding this allocation.";
            return;
        }

        IsSaving = true;
        DrawerErrorMessage = null;

        try
        {
            var voided = await _settlementService.VoidPaymentAllocationAsync(alloc.Id, AllocationVoidReason.Trim(), ct);
            AllocationVoidReason = string.Empty;

            // Refresh allocations list
            var index = _pvAllocations.IndexOf(alloc);
            if (index >= 0)
            {
                _pvAllocations[index] = voided;
            }

            // Reload open invoices
            if (SelectedPaymentVoucher?.SupplierId != null)
            {
                var invoices = await _settlementService.GetSupplierOpenInvoicesAsync(SelectedPaymentVoucher.SupplierId.Value, ct);
                _openPurchaseInvoices.Clear();
                foreach (var inv in invoices) _openPurchaseInvoices.Add(inv);
                SelectedPurchaseInvoice = _openPurchaseInvoices.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            DrawerErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }
}


public class JournalLineEntryModel : ViewModelBase
{
    private int _accountId;
    private decimal _debit;
    private decimal _credit;
    private int? _customerId;
    private int? _supplierId;
    private string? _description;

    public int AccountId { get => _accountId; set => SetField(ref _accountId, value); }
    public decimal Debit { get => _debit; set { if (SetField(ref _debit, value) && value > 0) Credit = 0; } }
    public decimal Credit { get => _credit; set { if (SetField(ref _credit, value) && value > 0) Debit = 0; } }
    public int? CustomerId { get => _customerId; set => SetField(ref _customerId, value); }
    public int? SupplierId { get => _supplierId; set => SetField(ref _supplierId, value); }
    public string? Description { get => _description; set => SetField(ref _description, value); }
}
