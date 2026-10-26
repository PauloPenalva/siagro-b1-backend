using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Interfaces;

/// <summary>Códigos de permissão consumidos pelo sistema. O cadastro é livre; estes têm efeito.</summary>
public static class PermissionCodes
{
    /// <summary>Digitar o peso manualmente na pesagem, em vez de capturá-lo da balança.</summary>
    public const string WeighingManualEntry = "WEIGHING_MANUAL_ENTRY";

    /// <summary>Editar o complemento fiscal do contrato de venda.</summary>
    public const string SalesContractFiscalEdit = "SALES_CONTRACT_FISCAL_EDIT";

    /// <summary>Liberar o pagamento do contrato de compra por departamento (GAC-1188).</summary>
    public const string PaymentReleaseStock = "PAYMENT_RELEASE_STOCK";
    public const string PaymentReleaseLegal = "PAYMENT_RELEASE_LEGAL";
    public const string PaymentReleaseFiscal = "PAYMENT_RELEASE_FISCAL";
    public const string PaymentReleaseRoyalties = "PAYMENT_RELEASE_ROYALTIES";
    public const string PaymentReleaseFinancial = "PAYMENT_RELEASE_FINANCIAL";
    public const string PaymentReleaseStore = "PAYMENT_RELEASE_STORE";
    public const string PaymentReleaseBoard = "PAYMENT_RELEASE_BOARD";

    /// <summary>Código de permissão que libera o pagamento pelo departamento.</summary>
    public static string ForDepartment(PaymentReleaseDepartment department) => department switch
    {
        PaymentReleaseDepartment.Stock => PaymentReleaseStock,
        PaymentReleaseDepartment.Legal => PaymentReleaseLegal,
        PaymentReleaseDepartment.Fiscal => PaymentReleaseFiscal,
        PaymentReleaseDepartment.Royalties => PaymentReleaseRoyalties,
        PaymentReleaseDepartment.Financial => PaymentReleaseFinancial,
        PaymentReleaseDepartment.Store => PaymentReleaseStore,
        PaymentReleaseDepartment.Board => PaymentReleaseBoard,
        _ => throw new ArgumentOutOfRangeException(nameof(department), department, null),
    };
}

public interface IUserPermissions
{
    Task<bool> HasAsync(string username, string permissionCode);

    Task<List<string>> GetAsync(string username);

    Task<bool> HasRoleAsync(string username, string roleCode);
}
