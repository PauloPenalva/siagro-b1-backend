namespace SiagroB1.Domain.Enums;

/// <summary>Rótulos pt-BR de <see cref="TransitLossReason"/>, os mesmos da tela (GAC-1213, spec §4.1).</summary>
public static class TransitLossReasonText
{
    public static string Describe(TransitLossReason reason) => reason switch
    {
        TransitLossReason.Damage => "Avaria / mercadoria derrubada",
        TransitLossReason.Accident => "Sinistro",
        TransitLossReason.Theft => "Roubo de carga",
        TransitLossReason.Other => "Outros",
        _ => reason.ToString(),
    };
}
