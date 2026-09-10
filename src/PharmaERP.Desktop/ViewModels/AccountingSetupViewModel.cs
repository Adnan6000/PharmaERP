using System.Windows.Input;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Desktop.ViewModels;

public class AccountingSetupViewModel : ViewModelBase
{
    private readonly IAccountingSetupService _setupService;
    private readonly IAccountingConfigService _configService;
    private readonly IAccountingReconciliationService _reconService;
    private readonly ConnectionStateStore _connectionStore;

    private AccountingSetupState _setupState = AccountingSetupState.NotConfigured;
    private bool _isLoading;
    private bool _isActionRunning;
    private string? _statusMessage;
    private string? _errorMessage;

    // Lock Date
    private DateTime? _currentLockDate;
    private DateTime _newLockDate = DateTime.Today;

    // Reconciliation Result
    private ReconciliationReportDto? _reconResult;

    public AccountingSetupViewModel(
        IAccountingSetupService setupService,
        IAccountingConfigService configService,
        IAccountingReconciliationService reconService,
        ConnectionStateStore connectionStore)
    {
        _setupService = setupService;
        _configService = configService;
        _reconService = reconService;
        _connectionStore = connectionStore;

        RefreshCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await LoadStateAsync(ct),
            canExecute: _ => !IsLoading && !IsActionRunning);

        InitializeAccountingCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await RunSetupAndBackfillAsync(ct),
            canExecute: _ => !IsLoading && !IsActionRunning);

        SetLockDateCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await SetLockDateAsync(ct),
            canExecute: _ => !IsLoading && !IsActionRunning);

        ClearLockDateCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await ClearLockDateAsync(ct),
            canExecute: _ => !IsLoading && !IsActionRunning && CurrentLockDate.HasValue);

        RunReconciliationCommand = new AsyncRelayCommand(
            execute: async (_, ct) => await RunReconciliationAsync(ct),
            canExecute: _ => !IsLoading && !IsActionRunning);
    }

    public AccountingSetupState SetupState { get => _setupState; set => SetField(ref _setupState, value); }
    public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
    public bool IsActionRunning { get => _isActionRunning; set => SetField(ref _isActionRunning, value); }
    public string? StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public string? ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }

    public DateTime? CurrentLockDate { get => _currentLockDate; set => SetField(ref _currentLockDate, value); }
    public DateTime NewLockDate { get => _newLockDate; set => SetField(ref _newLockDate, value); }
    public ReconciliationReportDto? ReconResult { get => _reconResult; set => SetField(ref _reconResult, value); }

    public ICommand RefreshCommand { get; }
    public ICommand InitializeAccountingCommand { get; }
    public ICommand SetLockDateCommand { get; }
    public ICommand ClearLockDateCommand { get; }
    public ICommand RunReconciliationCommand { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadStateAsync(ct);
        if (SetupState == AccountingSetupState.Active)
        {
            await RunReconciliationAsync(ct);
        }
    }

    private async Task LoadStateAsync(CancellationToken ct = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var status = await _configService.GetStatusAsync(ct);
            SetupState = status.State;
            CurrentLockDate = status.AccountingLockDate.HasValue ? status.AccountingLockDate.Value.ToDateTime(TimeOnly.MinValue) : null;
            if (CurrentLockDate.HasValue)
            {
                NewLockDate = CurrentLockDate.Value;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load accounting setup state: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RunSetupAndBackfillAsync(CancellationToken ct = default)
    {
        if (IsActionRunning) return;
        IsActionRunning = true;
        StatusMessage = "Initializing Chart of Accounts, system mappings, and backfilling historical transactions...";
        ErrorMessage = null;

        try
        {
            var result = await _setupService.RunHistoricalInitializationAsync(ct);
            SetupState = result.FinalState;
            StatusMessage = $"Accounting setup finished. Success: {result.Success}. State: {result.FinalState}. Created {result.TotalJournalsCreated} journals (Skipped: {result.TotalJournalsSkipped}).";
            if (result.ReconciliationReport != null)
            {
                ReconResult = result.ReconciliationReport;
            }
            else
            {
                await RunReconciliationAsync(ct);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Accounting setup / backfill failed: {ex.Message}";
            var status = await _configService.GetStatusAsync(ct);
            SetupState = status.State;
        }
        finally
        {
            IsActionRunning = false;
        }
    }

    private async Task SetLockDateAsync(CancellationToken ct = default)
    {
        if (IsActionRunning) return;
        IsActionRunning = true;
        ErrorMessage = null;

        try
        {
            var lockDate = DateOnly.FromDateTime(NewLockDate);
            await _configService.SetAccountingLockDateAsync(lockDate, ct);
            CurrentLockDate = NewLockDate;
            StatusMessage = $"Accounting lock date set to {lockDate:yyyy-MM-dd}. Transactions on or before this date are strictly frozen.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to set lock date: {ex.Message}";
        }
        finally
        {
            IsActionRunning = false;
        }
    }

    private async Task ClearLockDateAsync(CancellationToken ct = default)
    {
        if (IsActionRunning) return;
        IsActionRunning = true;
        ErrorMessage = null;

        try
        {
            await _configService.SetAccountingLockDateAsync(null, ct);
            CurrentLockDate = null;
            StatusMessage = "Accounting lock date removed. All periods unlocked.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to clear lock date: {ex.Message}";
        }
        finally
        {
            IsActionRunning = false;
        }
    }

    private async Task RunReconciliationAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ReconResult = await _reconService.RunReconciliationAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Reconciliation check error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
