namespace SiagroB1.Application.Interfaces;

/// <summary>Códigos de permissão consumidos pelo sistema. O cadastro é livre; estes têm efeito.</summary>
public static class PermissionCodes
{
    /// <summary>Digitar o peso manualmente na pesagem, em vez de capturá-lo da balança.</summary>
    public const string WeighingManualEntry = "WEIGHING_MANUAL_ENTRY";

    /// <summary>Editar o complemento fiscal do contrato de venda.</summary>
    public const string SalesContractFiscalEdit = "SALES_CONTRACT_FISCAL_EDIT";
}

public interface IUserPermissions
{
    Task<bool> HasAsync(string username, string permissionCode);

    Task<List<string>> GetAsync(string username);

    Task<bool> HasRoleAsync(string username, string roleCode);
}
