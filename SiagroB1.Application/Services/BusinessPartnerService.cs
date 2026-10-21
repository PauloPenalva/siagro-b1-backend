using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class BusinessPartnerService(
    IUnitOfWork db, 
    ILogger<BusinessPartnerService> logger,
    IStringLocalizer<Resource> resource,
    DocNumberSequenceService docNumbers,
    IConfiguration? configuration = null,
    AccountingAssignmentValidator? dimensionValidator = null
    ) 
    : IBusinessPartnerService
{
    public Task<IEnumerable<BusinessPartnerModel>> GetAllAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<BusinessPartnerModel?> GetByIdAsync(string code)
    {
        try
        {
            return await db.Context.BusinessPartners
                .Include(a => a.Addresses)
                .Select(x => new BusinessPartnerModel()
                {
                    CardCode = x.CardCode,
                    DocNumberKey = x.DocNumberKey,
                    CardName = x.CardName,
                    CardFName = x.CardFName,
                    CardType = x.CardType,
                    Notes = x.Notes,
                    QryGroup23 = x.QryGroup23,
                    TaxId = x.TaxId,
                    StateRegistration = x.StateRegistration,
                    StateRegistrationIndicator = x.StateRegistrationIndicator,
                    NfeEmails = x.NfeEmails.OrderBy(e => e.LineNum).Select(e => e.Email).ToList(),
                    Phone = x.Phone,
                    PaymentConditionCode = x.PaymentConditionCode,
                    IsRuralProducer = x.IsRuralProducer,
                    RuralProducerType = x.RuralProducerType,
                    RuralProducerCpf = x.RuralProducerCpf,
                    RuralPayrollOption = x.RuralPayrollOption,
                    DefaultCostCenterCode = x.DefaultCostCenterCode,
                    DefaultAccountingItemCode = x.DefaultAccountingItemCode,
                    // O Include acima é decorativo sem esta projeção: o EF materializa o
                    // Model, não a entidade, e a coleção voltava SEMPRE vazia. Quem depende
                    // dela é a resolução de CFOP (UF do destinatário), que rejeitaria todo
                    // documento de saída em modo STANDALONE. O equivalente em SAPB1 já
                    // projeta o endereço.
                    Addresses = x.Addresses
                        .Select(a => new AddressModel()
                        {
                            CardCode = a.CardCode,
                            AddressName = a.AddressName,
                            AdresType = a.AdresType,
                            Block = a.Block,
                            City = a.City,
                            Country = a.Country,
                            State = a.State,
                            Street = a.Street,
                            ZipCode = a.ZipCode,
                            StreetNumber = a.StreetNumber,
                            Complement = a.Complement,
                            MunicipalityCode = a.MunicipalityCode,
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync(x => x.CardCode == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException(ex.Message);
        }
    }

    public async Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel model)
    {
        NormalizeRuralProducer(model);
        ValidateRuralProducer(model);
        ValidateNfeFields(model);
        await ValidateDefaultAssignmentAsync(model, previous: null);

        foreach (var address in model.Addresses)
            AddressTypes.EnsureValid(address.AdresType, resource);

        await ResolveCardCodeAsync(model);

        var entity = new BusinessPartner()
        {
            CardCode = model.CardCode,
            DocNumberKey = model.DocNumberKey,
            CardName = model.CardName,
            CardFName = model.CardFName,
            CardType = model.CardType,
            QryGroup23 = WarehouseFlag(model.QryGroup23),
            TaxId = model.TaxId,
            StateRegistration = model.StateRegistration,
            StateRegistrationIndicator = model.StateRegistrationIndicator,
            Phone = model.Phone,
            PaymentConditionCode = model.PaymentConditionCode,
            IsRuralProducer = model.IsRuralProducer,
            RuralProducerType = model.RuralProducerType,
            RuralProducerCpf = model.RuralProducerCpf,
            RuralPayrollOption = model.RuralPayrollOption,
            DefaultCostCenterCode = model.DefaultCostCenterCode,
            DefaultAccountingItemCode = model.DefaultAccountingItemCode,
        };

        // A tela cria o parceiro com os endereços aninhados (deep insert). Sem copiá-los o
        // parceiro nascia sem endereço, e é do endereço de faturamento que sai a UF do CFOP e
        // do cálculo de tributos do documento de saída.
        foreach (var address in model.Addresses)
        {
            var entityAddress = new Address
            {
                CardCode = model.CardCode,
                AddressName = address.AddressName,
                AdresType = address.AdresType,
                Street = address.Street,
                StreetNumber = address.StreetNumber,
                Complement = address.Complement,
                Block = address.Block,
                ZipCode = address.ZipCode,
                City = address.City,
                State = address.State,
                Country = address.Country,
                MunicipalityCode = address.MunicipalityCode,
            };

            await AddressMunicipalityResolver.ApplyAsync(db.Context, entityAddress);
            entity.Addresses.Add(entityAddress);
        }

        ReplaceNfeEmails(entity, model.NfeEmails);

        await db.Context.BusinessPartners.AddAsync(entity);
        await db.SaveChangesAsync();
        return model;
    }

    // Cliente e Fornecedor têm série própria; qualquer outro tipo mantém o código digitado.
    // CardCode é uma tabela só: o código de um cliente também bloqueia o de um fornecedor.
    // Roda depois das validações para que um parceiro recusado não consuma número.
    private async Task ResolveCardCodeAsync(BusinessPartnerModel model)
    {
        var typed = model.CardCode?.Trim();
        var transactionCode = model.CardType switch
        {
            "C" => (TransactionCode?)TransactionCode.Customer,
            "S" => TransactionCode.Supplier,
            _ => null,
        };

        // Sem série e com código digitado (aba antiga, cliente de API): comportamento legado.
        var legacy = transactionCode is null || (model.DocNumberKey is null && !string.IsNullOrWhiteSpace(typed));

        if (legacy)
        {
            if (string.IsNullOrWhiteSpace(typed))
                throw new DefaultException("Informe o código do parceiro.");

            if (await db.Context.BusinessPartners.AnyAsync(b => b.CardCode == typed))
                throw new DefaultException(resource["BP_EXISTING_CODE"]);

            model.CardCode = typed;
            model.DocNumberKey = null;
            return;
        }

        var assignment = await docNumbers.AssignAsync(new DocNumberRequest(
            transactionCode!.Value, model.DocNumberKey, typed, 15,
            code => db.Context.BusinessPartners.AnyAsync(b => b.CardCode == code)));
        model.CardCode = assignment.Code;
        model.DocNumberKey = assignment.DocNumberKey;
    }

    public async Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel model)
    {
        NormalizeRuralProducer(model);
        ValidateRuralProducer(model);
        ValidateNfeFields(model);

        var entity = await db.Context.BusinessPartners
            .Include(a => a.Addresses)
            .Include(a => a.NfeEmails)
            .FirstOrDefaultAsync(x => x.CardCode == model.CardCode);

        if (entity == null)
            throw new NotFoundException(resource["BP_NOT_FOUND"]);

        await ValidateDefaultAssignmentAsync(model, new AccountingAssignmentSnapshot
        {
            CostCenterCode = entity.DefaultCostCenterCode,
            AccountingItemCode = entity.DefaultAccountingItemCode,
        });
        
        db.Context.Entry(entity).State = EntityState.Modified;

        entity.CardName = model.CardName;
        entity.CardFName = model.CardFName;
        entity.CardType = model.CardType;
        entity.Notes = model.Notes;
        entity.TaxId = model.TaxId;
        entity.StateRegistration = model.StateRegistration;
        entity.StateRegistrationIndicator = model.StateRegistrationIndicator;
        entity.Phone = model.Phone;
        entity.PaymentConditionCode = model.PaymentConditionCode;
        entity.IsRuralProducer = model.IsRuralProducer;
        entity.RuralProducerType = model.RuralProducerType;
        entity.RuralProducerCpf = model.RuralProducerCpf;
        entity.RuralPayrollOption = model.RuralPayrollOption;
        entity.DefaultCostCenterCode = model.DefaultCostCenterCode;
        entity.DefaultAccountingItemCode = model.DefaultAccountingItemCode;

        ReplaceNfeEmails(entity, model.NfeEmails);

        // Corpo sem a marca (cliente antigo, PUT parcial) não desmarca o armazém em silêncio.
        if (model.QryGroup23 is not null)
            entity.QryGroup23 = WarehouseFlag(model.QryGroup23);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException e)
        {
           throw new DefaultException(e.Message);
        }

        // A série é imutável: devolve a gravada, não a que veio no corpo.
        model.DocNumberKey = entity.DocNumberKey;
        return model;
    }

    /// <summary>
    /// Centro de custo e item contábil padrão do parceiro. Só um código novo é validado: o gravado que foi
    /// inativado depois continua aceito. Só STANDALONE.
    /// </summary>
    private async Task ValidateDefaultAssignmentAsync(BusinessPartnerModel model, AccountingAssignmentSnapshot? previous)
    {
        if (configuration is null || !ErpMode.IsStandalone(configuration) || dimensionValidator is null)
            return;

        var fields = new AccountingAssignmentSnapshot
        {
            CostCenterCode = model.DefaultCostCenterCode,
            AccountingItemCode = model.DefaultAccountingItemCode,
        };
        await dimensionValidator.NormalizeAndValidateAsync(fields, previous);
        model.DefaultCostCenterCode = fields.CostCenterCode;
        model.DefaultAccountingItemCode = fields.AccountingItemCode;
    }

    public async Task<bool> DeleteAsync(string code)
    {
        var entity = await db.Context.BusinessPartners
            .Include(x => x.Addresses)
            .Include(x => x.NfeEmails)
            .FirstOrDefaultAsync(x => x.CardCode == code);
        if (entity == null)
        {
            return false;
        }

        // Os documentos guardam o código do parceiro sem FK (em SAPB1 o parceiro vive no SAP), então
        // nada no banco impede a exclusão. Na CEAGUI (14/10/2026) o PN F001 foi excluído depois de virar
        // fornecedor e local de entrega de um contrato e recadastrado como F00011: a liberação sumiu
        // do "Expedir" da carga, que filtra pelo armazém do cadastro vivo.
        var usage = await FindUsageAsync(code);
        if (usage != null)
        {
            throw new DefaultException(
                $"O parceiro {code} não pode ser excluído: está em uso em {usage}.");
        }

        // BUSINESS_PARTNERS_ADDRESSES tem FK para o parceiro com NoAction: sem remover os endereços
        // antes, o DELETE do parceiro batia na FK (erro 547) e a tela recebia 500.
        db.Context.Addresses.RemoveRange(entity.Addresses);
        // A FK dos e-mails de NF-e é cascata no banco; remover explicitamente mantém o rastreamento coerente.
        db.Context.BusinessPartnerNfeEmails.RemoveRange(entity.NfeEmails);
        // Signatários não têm FK (em SAPB1 o parceiro vive no SAP), então não bloqueiam o DELETE, mas
        // ficariam órfãos e reapareceriam se o código fosse recriado. Minutas só os leem no envio.
        db.Context.BusinessPartnerSignatories.RemoveRange(
            await db.Context.BusinessPartnerSignatories.Where(s => s.CardCode == code).ToListAsync());
        db.Context.BusinessPartners.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Primeiro documento que referencia o parceiro (como cliente/fornecedor, armazém, local de entrega
    /// ou transportadora), ou <c>null</c>. Complemento do armazém, endereços e signatários são cadastro
    /// do próprio parceiro e não contam.
    /// </summary>
    private async Task<string?> FindUsageAsync(string code)
    {
        var c = db.Context;
        var checks = new (string Description, Func<Task<bool>> Exists)[]
        {
            ("Contrato de compra", () => c.PurchaseContracts.AnyAsync(x => x.CardCode == code || x.DeliveryLocationCode == code)),
            ("Corretor de contrato de compra", () => c.PurchaseContractsBrokers.AnyAsync(x => x.CardCode == code)),
            ("Contrato de venda", () => c.SalesContracts.AnyAsync(x => x.CardCode == code)),
            ("Local de entrega de contrato de venda", () => c.SalesContractsDeliveryLocations.AnyAsync(x => x.CardCode == code)),
            ("Liberação de entrega", () => c.ShipmentReleases.AnyAsync(x => x.DeliveryLocationCode == code)),
            ("Liberação de entrega de venda", () => c.SalesShipmentReleases.AnyAsync(x => x.DeliveryLocationCode == code)),
            ("Romaneio", () => c.StorageTransactions.AnyAsync(x => x.CardCode == code || x.WarehouseCode == code)),
            ("Lote de armazenagem", () => c.StorageAddresses.AnyAsync(x => x.CardCode == code || x.WarehouseCode == code)),
            ("Carga", () => c.ShipmentLoads.AnyAsync(x => x.CardCode == code || x.WarehouseCode == code || x.CarrierCardCode == code)),
            ("Transbordo de carga", () => c.ShipmentLoadsTransshipments.AnyAsync(x => x.WarehouseCode == code)),
            ("Recusa de carga", () => c.ShipmentLoadRefusals.AnyAsync(x => x.DestinationWarehouseCode == code)),
            ("Ticket de pesagem", () => c.WeighingTickets.AnyAsync(x => x.CardCode == code)),
            ("Documento de saída", () => c.SalesInvoices.AnyAsync(x => x.CardCode == code || x.DeliveryCardCode == code)),
            ("Documento de entrada", () => c.PurchaseInvoices.AnyAsync(x => x.CardCode == code)),
            ("Fatura de armazenagem", () => c.StorageInvoices.AnyAsync(x => x.CardCode == code)),
            ("Conferência de armazém", () => c.WarehouseReconciliations.AnyAsync(x => x.CardCode == code || x.WarehouseCode == code)),
            ("Título financeiro", () => c.FinancialDocuments.AnyAsync(x => x.CardCode == code)),
        };

        foreach (var (description, exists) in checks)
        {
            if (await exists())
            {
                return description;
            }
        }

        return null;
    }

    public IQueryable<BusinessPartnerModel> QueryAll()
    {
        return db.Context.BusinessPartners
            .Include(a => a.Addresses)
            .Select(x => new BusinessPartnerModel()
            {
                CardCode = x.CardCode,
                DocNumberKey = x.DocNumberKey,
                CardName = x.CardName,
                CardFName = x.CardFName,
                CardType = x.CardType,
                Notes = x.Notes,
                QryGroup23 = x.QryGroup23,
                TaxId = x.TaxId,
                StateRegistration = x.StateRegistration,
                StateRegistrationIndicator = x.StateRegistrationIndicator,
                NfeEmails = x.NfeEmails.OrderBy(e => e.LineNum).Select(e => e.Email).ToList(),
                Phone = x.Phone,
                PaymentConditionCode = x.PaymentConditionCode,
                IsRuralProducer = x.IsRuralProducer,
                RuralProducerType = x.RuralProducerType,
                RuralProducerCpf = x.RuralProducerCpf,
                RuralPayrollOption = x.RuralPayrollOption,
                DefaultCostCenterCode = x.DefaultCostCenterCode,
                    DefaultAccountingItemCode = x.DefaultAccountingItemCode,
                Addresses = x.Addresses
                    .Where(a => a.CardCode == x.CardCode)
                    .Select(a => new AddressModel()
                    {
                        // Parte da chave do endereço no EDM: nulo aqui derrubava com 500 todo $expand de Addresses.
                        CardCode = a.CardCode,
                        AddressName = a.AddressName,
                        AdresType = a.AdresType,
                        Block = a.Block,
                        City = a.City,
                        Country = a.Country,
                        State = a.State,
                        Street = a.Street,
                        ZipCode = a.ZipCode,
                        StreetNumber = a.StreetNumber,
                        Complement = a.Complement,
                        MunicipalityCode = a.MunicipalityCode,
                    })
                    .AsQueryable()
                    .ToList()
            })
            .AsNoTracking();
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    public async Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(IReadOnlyCollection<string> cardCodes)
    {
        if (cardCodes.Count == 0)
        {
            return new Dictionary<string, SupplierInfo>();
        }

        return await db.Context.BusinessPartners
            .AsNoTracking()
            .Where(bp => cardCodes.Contains(bp.CardCode))
            .Select(bp => new SupplierInfo
            {
                CardCode = bp.CardCode,
                CardFName = bp.CardFName,
                CardName = bp.CardName,
                TaxId = bp.TaxId,
                Notes = bp.Notes,
                Address = bp.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => new SupplierAddress
                    {
                        City = a.City,
                        State = a.State
                    })
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.CardCode);
    }
    
    /// <summary>
    /// Coerência dos campos da NF-e. O serviço local só existe em STANDALONE, então a regra já
    /// nasce restrita ao modo. Contribuinte (indicador 1) exige IE só com dígitos — é o tipo do
    /// schema para <c>dest/IE</c>; isento e não contribuinte não levam IE no XML.
    /// </summary>
    private static void ValidateNfeFields(BusinessPartnerModel model)
    {
        if (model.StateRegistrationIndicator == StateRegistrationIndicator.Taxpayer)
        {
            if (string.IsNullOrWhiteSpace(model.StateRegistration))
                throw new DefaultException("Parceiro contribuinte do ICMS precisa da inscrição estadual.");

            if (!System.Text.RegularExpressions.Regex.IsMatch(model.StateRegistration.Trim(), "^[0-9]{2,14}$"))
                throw new DefaultException("A inscrição estadual do contribuinte deve ter só dígitos (2 a 14).");
        }

        NormalizeNfeEmails(model);
    }

    /// <summary>Apara, descarta vazios e valida formato, duplicidade (sem diferenciar caixa) e o máximo de 10.</summary>
    private static void NormalizeNfeEmails(BusinessPartnerModel model) =>
        model.NfeEmails = Services.NfeEmail.NfeEmailAddresses.Normalize(model.NfeEmails);

    private void ReplaceNfeEmails(BusinessPartner entity, List<string> emails)
    {
        // EF ordena DELETE antes de INSERT no mesmo save, então reaproveitar um e-mail não colide no índice único.
        db.Context.BusinessPartnerNfeEmails.RemoveRange(entity.NfeEmails.ToList());
        entity.NfeEmails.Clear();
        for (var k = 0; k < emails.Count; k++)
        {
            var line = new BusinessPartnerNfeEmail { CardCode = entity.CardCode, Email = emails[k], LineNum = k };
            entity.NfeEmails.Add(line);
            // A chave já vem preenchida e o parceiro está marcado Modified: sem o Add explícito o EF trataria a linha como existente (UPDATE).
            db.Context.BusinessPartnerNfeEmails.Add(line);
        }
    }

    /// <summary>
    /// Deixa os campos de produtor rural coerentes antes de validar e gravar: quem não é produtor
    /// não carrega tipo, CPF nem opção pela folha; pessoa jurídica não tem CPF nem folha. O CPF
    /// é gravado só com dígitos.
    /// </summary>
    private static void NormalizeRuralProducer(BusinessPartnerModel model)
    {
        if (!model.IsRuralProducer)
        {
            model.RuralProducerType = null;
            model.RuralProducerCpf = null;
            model.RuralPayrollOption = false;
            return;
        }

        var cpf = new string((model.RuralProducerCpf ?? "").Where(char.IsAsciiDigit).ToArray());
        model.RuralProducerCpf = cpf.Length == 0 ? null : cpf;

        if (model.RuralProducerType == RuralProducerType.LegalEntity)
        {
            model.RuralProducerCpf = null;
            model.RuralPayrollOption = false;
        }
    }

    /// <summary>
    /// Produtor rural exige o tipo; pessoa física/segurado especial cadastrado com CNPJ exige o
    /// CPF do produtor (com CPF no <c>TaxId</c> o próprio documento já o identifica).
    /// </summary>
    private static void ValidateRuralProducer(BusinessPartnerModel model)
    {
        if (!model.IsRuralProducer) return;

        if (model.RuralProducerType is null)
            throw new DefaultException("Informe o tipo de produtor rural.");

        if (model.RuralProducerType == RuralProducerType.LegalEntity) return;

        var taxIdDigits = new string((model.TaxId ?? "").Where(char.IsAsciiDigit).ToArray());
        var cpfRequired = taxIdDigits.Length == 14;

        if (string.IsNullOrEmpty(model.RuralProducerCpf))
        {
            if (cpfRequired)
                throw new DefaultException("Informe o CPF do produtor rural.");
            return;
        }

        // TaxIdValidator também aceita CNPJ (14): o CPF do produtor tem de ter 11 dígitos.
        if (model.RuralProducerCpf.Length != 11 ||
            !SiagroB1.Commons.Validation.TaxIdValidator.IsValid(model.RuralProducerCpf))
            throw new DefaultException("CPF do produtor rural inválido.");
    }

    private bool EntityExists(string code)
    {
        return db.Context.BusinessPartners.Any(e => e.CardCode == code);
    }

    /// <summary>QryGroup23 = 'Y' marca o parceiro como ARMAZÉM; qualquer outro valor vira 'N'.</summary>
    private static string WarehouseFlag(string? value) => value == "Y" ? "Y" : "N";
}