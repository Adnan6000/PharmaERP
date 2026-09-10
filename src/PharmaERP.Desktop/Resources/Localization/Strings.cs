using System.Globalization;
using System.Resources;

namespace PharmaERP.Desktop.Resources.Localization;

public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new("PharmaERP.Desktop.Resources.Localization.Strings", typeof(Strings).Assembly);

    public static string Get(string key, CultureInfo? culture = null)
    {
        try
        {
            return ResourceManager.GetString(key, culture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    // Strongly-typed accessors for key UI tokens
    public static string Nav_Dashboard => Get(nameof(Nav_Dashboard));
    public static string Nav_POS => Get(nameof(Nav_POS));
    public static string Nav_Sales => Get(nameof(Nav_Sales));
    public static string Nav_SalesReturns => Get(nameof(Nav_SalesReturns));
    public static string Nav_Products => Get(nameof(Nav_Products));
    public static string Nav_Inventory => Get(nameof(Nav_Inventory));
    public static string Nav_OpeningStock => Get(nameof(Nav_OpeningStock));
    public static string Nav_Purchases => Get(nameof(Nav_Purchases));
    public static string Nav_PurchaseEntry => Get(nameof(Nav_PurchaseEntry));
    public static string Nav_PurchaseReturns => Get(nameof(Nav_PurchaseReturns));
    public static string Nav_Customers => Get(nameof(Nav_Customers));
    public static string Nav_Suppliers => Get(nameof(Nav_Suppliers));
    public static string Nav_Manufacturers => Get(nameof(Nav_Manufacturers));
    public static string Nav_Categories => Get(nameof(Nav_Categories));
    public static string Nav_Units => Get(nameof(Nav_Units));
    public static string Nav_COA => Get(nameof(Nav_COA));
    public static string Nav_Vouchers => Get(nameof(Nav_Vouchers));
    public static string Nav_Ledgers => Get(nameof(Nav_Ledgers));
    public static string Nav_AccountingSetup => Get(nameof(Nav_AccountingSetup));
    public static string Nav_Settings => Get(nameof(Nav_Settings));

    public static string Common_Save => Get(nameof(Common_Save));
    public static string Common_Cancel => Get(nameof(Common_Cancel));
    public static string Common_Delete => Get(nameof(Common_Delete));
    public static string Common_Refresh => Get(nameof(Common_Refresh));
    public static string Common_Search => Get(nameof(Common_Search));
    public static string Common_Print => Get(nameof(Common_Print));
    public static string Common_TotalPayable => Get(nameof(Common_TotalPayable));
    public static string Common_CashTendered => Get(nameof(Common_CashTendered));
    public static string Common_ChangeReturned => Get(nameof(Common_ChangeReturned));
    public static string Common_GrossTotal => Get(nameof(Common_GrossTotal));
    public static string Common_NetTotal => Get(nameof(Common_NetTotal));
    public static string Common_Discount => Get(nameof(Common_Discount));
    public static string Common_Status => Get(nameof(Common_Status));
    public static string Common_Date => Get(nameof(Common_Date));
    public static string Common_Reference => Get(nameof(Common_Reference));
    public static string Common_Narration => Get(nameof(Common_Narration));
    public static string Common_Debit => Get(nameof(Common_Debit));
    public static string Common_Credit => Get(nameof(Common_Credit));
    public static string Common_Balance => Get(nameof(Common_Balance));
    public static string Common_Active => Get(nameof(Common_Active));
    public static string Common_Inactive => Get(nameof(Common_Inactive));
}

