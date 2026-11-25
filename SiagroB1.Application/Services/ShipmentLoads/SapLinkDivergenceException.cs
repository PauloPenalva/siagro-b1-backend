namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Vínculo de NF do SAP com divergência (tipo 1 ou 2) pedido sem a confirmação do usuário (GAC-1226).
/// A mensagem é o texto fixo do alerta; nada foi gravado.
/// </summary>
public class SapLinkDivergenceException(SapLinkAssessment assessment) : ApplicationException(assessment.Message)
{
    public SapLinkAssessment Assessment { get; } = assessment;
}
