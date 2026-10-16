namespace SiagroB1.Security.Shared;

public static class CompanyClaims
{
    /// <summary>Só diagnóstico/log. Acesso a dados vai SEMPRE pelo ICompanyContext, nunca por esta claim.</summary>
    public const string CompanyCode = "CompanyCode";
}
