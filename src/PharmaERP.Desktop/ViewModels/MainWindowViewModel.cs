using System.Windows.Input;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ConnectionStateStore _connectionStore;
    private readonly DashboardViewModel _dashboardVm;
    private readonly SalesEntryViewModel _salesEntryVm;
    private readonly SalesViewModel _salesVm;
    private readonly SalesReturnsViewModel _salesReturnsVm;
    private readonly ProductsViewModel _productsVm;
    private readonly CustomersViewModel _customersVm;
    private readonly SuppliersViewModel _suppliersVm;
    private readonly ManufacturersViewModel _manufacturersVm;
    private readonly CategoriesViewModel _categoriesVm;
    private readonly UnitsViewModel _unitsVm;
    private readonly PurchaseEntryViewModel _purchaseEntryVm;
    private readonly PurchasesViewModel _purchasesVm;
    private readonly PurchaseReturnsViewModel _purchaseReturnsVm;
    private readonly InventoryStockViewModel _inventoryStockVm;
    private readonly OpeningStockViewModel _openingStockVm;
    private readonly SettingsViewModel _settingsVm;

    private object _currentViewModel;
    private string _selectedNavSection = "Dashboard";
    private string _windowTitle = "PharmaERP — Pharmacy Management & POS System";

    public MainWindowViewModel(
        ConnectionStateStore connectionStore,
        DashboardViewModel dashboardVm,
        SalesEntryViewModel salesEntryVm,
        SalesViewModel salesVm,
        SalesReturnsViewModel salesReturnsVm,
        ProductsViewModel productsVm,
        CustomersViewModel customersVm,
        SuppliersViewModel suppliersVm,
        ManufacturersViewModel manufacturersVm,
        CategoriesViewModel categoriesVm,
        UnitsViewModel unitsVm,
        PurchaseEntryViewModel purchaseEntryVm,
        PurchasesViewModel purchasesVm,
        PurchaseReturnsViewModel purchaseReturnsVm,
        InventoryStockViewModel inventoryStockVm,
        OpeningStockViewModel openingStockVm,
        SettingsViewModel settingsVm)
    {
        _connectionStore = connectionStore;
        _dashboardVm = dashboardVm;
        _salesEntryVm = salesEntryVm;
        _salesVm = salesVm;
        _salesReturnsVm = salesReturnsVm;
        _productsVm = productsVm;
        _customersVm = customersVm;
        _suppliersVm = suppliersVm;
        _manufacturersVm = manufacturersVm;
        _categoriesVm = categoriesVm;
        _unitsVm = unitsVm;
        _purchaseEntryVm = purchaseEntryVm;
        _purchasesVm = purchasesVm;
        _purchaseReturnsVm = purchaseReturnsVm;
        _inventoryStockVm = inventoryStockVm;
        _openingStockVm = openingStockVm;
        _settingsVm = settingsVm;

        _currentViewModel = _dashboardVm;
        _dashboardVm.SetNavigateAction(NavigateToSection);
        _purchasesVm.SetNavigateAction(NavigateToSection);
        _purchaseEntryVm.SetNavigateAction(NavigateToSection);

        NavigateCommand = new RelayCommand(
            execute: param =>
            {
                if (param is string section)
                {
                    NavigateToSection(section);
                }
            });

        SalesEntryHotkeyCommand = new RelayCommand(_ => NavigateToSection("SalesEntry"));
        PurchaseEntryHotkeyCommand = new RelayCommand(_ => NavigateToSection("PurchaseEntry"));
        RefreshCommand = new RelayCommand(_ => HandleRefresh());
        NewItemCommand = new RelayCommand(_ => HandleNewItem());
        CloseDrawerCommand = new RelayCommand(_ => HandleCloseDrawer());
        SaveItemCommand = new RelayCommand(_ => HandleSaveItem());

        _ = _connectionStore.CheckConnectionAsync(CancellationToken.None);
    }

    public ConnectionStateStore ConnectionStore => _connectionStore;

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetField(ref _windowTitle, value);
    }

    public string SelectedNavSection
    {
        get => _selectedNavSection;
        set => SetField(ref _selectedNavSection, value);
    }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        set => SetField(ref _currentViewModel, value);
    }

    public ICommand NavigateCommand { get; }
    public ICommand SalesEntryHotkeyCommand { get; }
    public ICommand PurchaseEntryHotkeyCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand NewItemCommand { get; }
    public ICommand CloseDrawerCommand { get; }
    public ICommand SaveItemCommand { get; }

    public void NavigateToSection(string section)
    {
        SelectedNavSection = section;

        CurrentViewModel = section switch
        {
            "Dashboard" => _dashboardVm,
            "SalesEntry" => _salesEntryVm,
            "Sales" => _salesVm,
            "SalesReturns" => _salesReturnsVm,
            "Purchases" => _purchasesVm,
            "PurchaseEntry" => _purchaseEntryVm,
            "PurchaseReturns" => _purchaseReturnsVm,
            "InventoryStock" => _inventoryStockVm,
            "OpeningStock" => _openingStockVm,
            "Products" => _productsVm,
            "Customers" => _customersVm,
            "Suppliers" => _suppliersVm,
            "Manufacturers" => _manufacturersVm,
            "Categories" => _categoriesVm,
            "Units" => _unitsVm,
            "Settings" => _settingsVm,
            _ => _dashboardVm
        };
    }

    private void HandleRefresh()
    {
        if (CurrentViewModel is DashboardViewModel d && d.RefreshCommand.CanExecute(null)) d.RefreshCommand.Execute(null);
        else if (CurrentViewModel is SalesViewModel s && s.SearchCommand.CanExecute(null)) s.SearchCommand.Execute(null);
        else if (CurrentViewModel is SalesReturnsViewModel sr && sr.RefreshHistoryCommand.CanExecute(null)) sr.RefreshHistoryCommand.Execute(null);
        else if (CurrentViewModel is ProductsViewModel p && p.RefreshCommand.CanExecute(null)) p.RefreshCommand.Execute(null);
        else if (CurrentViewModel is CustomersViewModel cu && cu.RefreshCommand.CanExecute(null)) cu.RefreshCommand.Execute(null);
        else if (CurrentViewModel is SuppliersViewModel su && su.RefreshCommand.CanExecute(null)) su.RefreshCommand.Execute(null);
        else if (CurrentViewModel is PurchasesViewModel pu && pu.RefreshCommand.CanExecute(null)) pu.RefreshCommand.Execute(null);
        else if (CurrentViewModel is PurchaseReturnsViewModel pr && pr.RefreshCommand.CanExecute(null)) pr.RefreshCommand.Execute(null);
        else if (CurrentViewModel is InventoryStockViewModel inv && inv.RefreshCommand.CanExecute(null)) inv.RefreshCommand.Execute(null);
        else if (CurrentViewModel is ManufacturersViewModel m && m.RefreshCommand.CanExecute(null)) m.RefreshCommand.Execute(null);
        else if (CurrentViewModel is CategoriesViewModel c && c.RefreshCommand.CanExecute(null)) c.RefreshCommand.Execute(null);
        else if (CurrentViewModel is UnitsViewModel u && u.RefreshCommand.CanExecute(null)) u.RefreshCommand.Execute(null);
    }

    private void HandleNewItem()
    {
        if (CurrentViewModel is SalesEntryViewModel se && se.ClearFormCommand.CanExecute(null)) se.ClearFormCommand.Execute(null);
        else if (CurrentViewModel is ProductsViewModel p && p.NewProductCommand.CanExecute(null)) p.NewProductCommand.Execute(null);
        else if (CurrentViewModel is CustomersViewModel cu && cu.NewCommand.CanExecute(null)) cu.NewCommand.Execute(null);
        else if (CurrentViewModel is SuppliersViewModel su && su.NewCommand.CanExecute(null)) su.NewCommand.Execute(null);
        else if (CurrentViewModel is PurchasesViewModel pu && pu.NewPurchaseEntryCommand.CanExecute(null)) pu.NewPurchaseEntryCommand.Execute(null);
        else if (CurrentViewModel is PurchaseReturnsViewModel pr && pr.OpenCreateDrawerCommand.CanExecute(null)) pr.OpenCreateDrawerCommand.Execute(null);
        else if (CurrentViewModel is ManufacturersViewModel m && m.NewCommand.CanExecute(null)) m.NewCommand.Execute(null);
        else if (CurrentViewModel is CategoriesViewModel c && c.NewCommand.CanExecute(null)) c.NewCommand.Execute(null);
        else if (CurrentViewModel is UnitsViewModel u && u.NewCommand.CanExecute(null)) u.NewCommand.Execute(null);
    }

    private void HandleCloseDrawer()
    {
        if (CurrentViewModel is SalesEntryViewModel se && se.EscapeCommand.CanExecute(null)) se.EscapeCommand.Execute(null);
        else if (CurrentViewModel is ProductsViewModel { IsDrawerOpen: true } p) p.CloseDrawer();
        else if (CurrentViewModel is CustomersViewModel { IsDrawerOpen: true } cu) cu.CloseDrawer();
        else if (CurrentViewModel is SuppliersViewModel { IsDrawerOpen: true } su) su.CloseDrawer();
        else if (CurrentViewModel is PurchasesViewModel { IsDetailsDrawerOpen: true } pud) pud.IsDetailsDrawerOpen = false;
        else if (CurrentViewModel is PurchasesViewModel { IsCancelDrawerOpen: true } puc) puc.IsCancelDrawerOpen = false;
        else if (CurrentViewModel is PurchaseReturnsViewModel { IsCreateDrawerOpen: true } prc) prc.IsCreateDrawerOpen = false;
        else if (CurrentViewModel is PurchaseReturnsViewModel { IsDetailsDrawerOpen: true } prd) prd.IsDetailsDrawerOpen = false;
        else if (CurrentViewModel is ManufacturersViewModel { IsDrawerOpen: true } m) m.CloseDrawer();
        else if (CurrentViewModel is CategoriesViewModel { IsDrawerOpen: true } c) c.CloseDrawer();
        else if (CurrentViewModel is UnitsViewModel { IsDrawerOpen: true } u) u.CloseDrawer();
    }

    private void HandleSaveItem()
    {
        if (CurrentViewModel is SalesEntryViewModel se && se.OpenTenderModalCommand.CanExecute(null))
            se.OpenTenderModalCommand.Execute(null);
        else if (CurrentViewModel is ProductsViewModel { IsDrawerOpen: true } p && p.SaveProductCommand.CanExecute(null))
            p.SaveProductCommand.Execute(null);
        else if (CurrentViewModel is CustomersViewModel { IsDrawerOpen: true } cu && cu.SaveCommand.CanExecute(null))
            cu.SaveCommand.Execute(null);
        else if (CurrentViewModel is SuppliersViewModel { IsDrawerOpen: true } su && su.SaveCommand.CanExecute(null))
            su.SaveCommand.Execute(null);
        else if (CurrentViewModel is ManufacturersViewModel { IsDrawerOpen: true } m && m.SaveCommand.CanExecute(null))
            m.SaveCommand.Execute(null);
        else if (CurrentViewModel is CategoriesViewModel { IsDrawerOpen: true } c && c.SaveCommand.CanExecute(null))
            c.SaveCommand.Execute(null);
        else if (CurrentViewModel is UnitsViewModel { IsDrawerOpen: true } u && u.SaveCommand.CanExecute(null))
            u.SaveCommand.Execute(null);
    }
}

