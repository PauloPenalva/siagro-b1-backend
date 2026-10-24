namespace SiagroB1.Domain.Enums;

/// <summary>Forma de apuração do lucro real na ECF (0010 FORMA_TRIB/FORMA_APUR).</summary>
public enum EcfAssessmentForm
{
    Annual = 1,
    Quarterly = 2,
}

/// <summary>Tributo a que o ajuste do LALUR/LACS se aplica.</summary>
public enum EcfAdjustmentTax
{
    Irpj = 1,
    Csll = 2,
    Both = 3,
}

/// <summary>Natureza do ajuste: adição (M300/M350), exclusão ou compensação de prejuízo fiscal.</summary>
public enum EcfAdjustmentKind
{
    Addition = 1,
    Exclusion = 2,
    LossOffset = 3,
}
