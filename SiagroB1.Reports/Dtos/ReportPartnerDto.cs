namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Dados do parceiro de negócios necessários aos relatórios, independentes de a
/// origem ser o banco do Siagro (standalone) ou o do SAP Business One.
/// </summary>
public class ReportPartnerDto
{
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public string? TaxId { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }

    /// <summary>
    /// Identificação fiscal detalhada e endereço em uma linha. Só são preenchidos na
    /// origem SAP (CRD7/CRD1/OCNT); na origem standalone ficam nulos.
    /// </summary>
    public string? Cnpj { get; set; }

    public string? Cpf { get; set; }

    public string? StateRegistration { get; set; }

    public string? FullAddress { get; set; }

    /// <summary>Sócios administradores em uma linha (OCPR com cargo "Socio").</summary>
    public string? ManagingPartners { get; set; }

    /// <summary>E-mail/celular para envio do contrato (OCPR com cargo "Contrato").</summary>
    public string? ContractContact { get; set; }
}
