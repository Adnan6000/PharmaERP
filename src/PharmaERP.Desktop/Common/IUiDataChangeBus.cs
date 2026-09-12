using System;

namespace PharmaERP.Desktop.Common;

public enum UiDataChangeType
{
    All,
    ProductChanged,
    CategoryChanged,
    ManufacturerChanged,
    UnitChanged,
    MasterChanged,
    CustomerChanged,
    SupplierChanged,
    PurchasePosted,
    PurchaseCancelled,
    PurchaseReturned,
    SalePosted,
    SaleCancelled,
    SaleReturned,
    StockChanged,
    VoucherPosted,
    VoucherVoided,
    AccountChanged,
    AccountingChanged,
    DatabaseConnectionChanged
}

public record UiDataChangeEvent(UiDataChangeType ChangeType, object? Payload = null);

public interface IUiDataChangeBus
{
    void Publish(UiDataChangeType changeType, object? payload = null);
    IDisposable Subscribe(Action<UiDataChangeType> handler);
    IDisposable Subscribe(Action<UiDataChangeEvent> handler);
}

