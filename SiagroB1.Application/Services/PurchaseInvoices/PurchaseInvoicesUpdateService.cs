using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Services.Nfe;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Atualiza o documento de entrada — cabeçalho E LINHAS.
///
/// As linhas são o ponto. A AMARRAÇÃO de cada linha com a nota de origem é manual e normalmente
/// feita DEPOIS de importar o XML, que não carrega esse vínculo. O serviço equivalente da devolução
/// antiga (<c>CustomerReturnsUpdateService</c>) nunca tocava a coleção, e com o Detail read-only
/// não existia caminho nenhum para amarrar depois de gravar.
///
/// Só documento PENDENTE é alterável: confirmado tem efeito de negócio pendurado (Fase 3) e precisa
/// passar pelo estorno.
/// </summary>
public class PurchaseInvoicesUpdateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    PurchaseInvoicesTaxApplyService taxApply,
    DimensionCodeValidator? dimensionValidator = null,
    DimensionDefaultsResolver? dimensionDefaults = null)
{
    public async Task ExecuteAsync(Guid key, PurchaseInvoice entity, string userName)
    {
        var existing = await db.Context.PurchaseInvoices
                           .Include(x => x.Items)
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (existing.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ser alterado. Estorne a confirmação antes.");

        // Capturado ANTES da cópia do corpo: é com isto que se sabe se a autorização da SEFAZ ficou velha.
        var storedAuthorizationScope = (Key: NormalizeKey(existing.ChaveNFe), existing.TaxDocumentKind, existing.IssuerType);

        // O Own vira NF-e logo abaixo, e o CT-e sumiria em silêncio: recusa antes (spec 2026-10-17).
        if (entity.IssuerType == DocumentIssuerType.Own && PurchaseInvoiceCteRules.IsCte(entity))
            throw new DefaultException(PurchaseInvoiceCteRules.OwnIssuerMessage);

        // O "antes" das dimensões do cabeçalho, capturado antes da cópia do corpo.
        var previousDims = DimensionSnapshot.FromOriginal(db.Context.Entry(existing).OriginalValues);

        existing.InvoiceType = entity.InvoiceType;
        existing.IssuerType = entity.IssuerType;
        existing.TaxDocumentKind = existing.IssuerType == DocumentIssuerType.Own ? TaxDocumentKind.Nfe : entity.TaxDocumentKind;

        // O emitente é editável enquanto o documento está pendente. Sem estas linhas a troca era
        // descartada em silêncio — a tela gravava sem erro e voltava com o emitente antigo.
        //
        // O NOME é re-resolvido quando o código muda, e não copiado: o value help grava a
        // descrição com group ID null de propósito, então o PATCH chega com o nome do emitente
        // ANTERIOR. Copiar direto deixava o documento com o código de um parceiro e o nome de
        // outro — e é o nome que a lista e os relatórios mostram.
        var issuerChanged = existing.CardCode != entity.CardCode;

        existing.CardCode = entity.CardCode;
        existing.CardName = entity.CardName;

        if (issuerChanged || string.IsNullOrWhiteSpace(existing.CardName))
            existing.CardName =
                (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName
                ?? entity.CardName;
        existing.TaxDocumentNumber = entity.TaxDocumentNumber;
        existing.TaxDocumentSeries = entity.TaxDocumentSeries;
        existing.ChaveNFe = entity.ChaveNFe;
        existing.IssueDate = entity.IssueDate;
        existing.PostingDate = entity.PostingDate;
        existing.TotalDocumentValue = entity.TotalDocumentValue;
        existing.TaxPayerComments = entity.TaxPayerComments;
        existing.Comments = entity.Comments;
        existing.GrossWeight = entity.GrossWeight;
        existing.NetWeight = entity.NetWeight;
        existing.VolumeQuantity = entity.VolumeQuantity;
        existing.VolumeSpecies = entity.VolumeSpecies;
        existing.VolumeBrand = entity.VolumeBrand;
        existing.VolumeNumbering = entity.VolumeNumbering;
        existing.TruckCode = entity.TruckCode;
        existing.TruckingCompanyCode = entity.TruckingCompanyCode;
        existing.TruckingCompanyName = entity.TruckingCompanyName;
        existing.FreightTerms = entity.FreightTerms;
        existing.BranchCode = entity.BranchCode;
        existing.PaymentConditionCode = entity.PaymentConditionCode;
        existing.WithholdingsManual = entity.WithholdingsManual;
        existing.WithholdingBase = entity.WithholdingBase;
        existing.WithheldIss = entity.WithheldIss;
        existing.WithheldIrrf = entity.WithheldIrrf;
        existing.WithheldInss = entity.WithheldInss;
        existing.WithheldPis = entity.WithheldPis;
        existing.WithheldCofins = entity.WithheldCofins;
        existing.WithheldCsll = entity.WithheldCsll;
        existing.FreightStartState = entity.FreightStartState;
        existing.FreightEndState = entity.FreightEndState;
        existing.FreightStartCity = entity.FreightStartCity;
        existing.FreightEndCity = entity.FreightEndCity;
        existing.FreightPayerTaxId = entity.FreightPayerTaxId;
        existing.CteIcmsCst = entity.CteIcmsCst;
        existing.CteIcmsValue = entity.CteIcmsValue;
        existing.PurchaseInvoiceOriginKey = entity.PurchaseInvoiceOriginKey;
        existing.CustodyStorageTransactionKey = entity.CustodyStorageTransactionKey;

        // Sair de CT-e: os vínculos com NF-e transportadas só fazem sentido no CT-e, e o ICMS do CT-e também.
        if (storedAuthorizationScope.TaxDocumentKind == TaxDocumentKind.Cte && existing.TaxDocumentKind != TaxDocumentKind.Cte)
        {
            if (await db.Context.PurchaseInvoiceTransportedDocuments.AnyAsync(x => x.PurchaseInvoiceKey == existing.Key))
                throw new DefaultException("Remova os vínculos de NF-e Referenciada(s) antes de mudar o tipo do documento.");

            existing.CteIcmsCst = null;
            existing.CteIcmsValue = null;
        }

        // Dimensões do cabeçalho: copiadas do corpo, completadas pelo padrão do fornecedor e validadas só no que mudou.
        // As linhas existentes não são reescritas (ver SyncItemsAsync).
        DimensionSnapshot.Copy(entity, existing);
        await DimensionWiring.ApplyAsync(dimensionDefaults, dimensionValidator, existing, null,
            existing.CardCode, null, previousDims);

        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = userName;

        // Travas da NF-e (spec §7): o corpo nunca escreve a emissão; a devolução de compra mantém o
        // cabeçalho que a identifica; emitido/em processamento tem o cabeçalho fiscal travado.
        var entry = db.Context.Entry(existing);
        PurchaseInvoiceNfeLock.RestoreIssuanceFields(entry);
        PurchaseInvoiceNfeLock.RestoreReturnHeader(entry);
        PurchaseInvoiceNfeLock.RestoreCounterNoteHeader(entry);
        PurchaseInvoiceNfeLock.EnsureHeaderEditable(entry);

        // Mesma recusa do Create: sem ela o PATCH trocaria o tipo de uma entrada própria para Devolução e
        // fugiria do botão Devolver. Depois da restauração acima, a devolução de compra de verdade mantém o tipo.
        if (existing.IssuerType == DocumentIssuerType.Own && existing.InvoiceType == PurchaseInvoiceType.Return &&
            !existing.IsNfeReturn && await taxApply.IsBranchActiveAsync(existing.BranchCode))
            throw new DefaultException(
                "Na filial que emite NF-e pelo Siagro, a devolução de compra é feita pelo botão Devolver, no detalhe do documento de entrada.");

        // O CT-e vale pela chave em qualquer filial, com ou sem a regra fiscal ativa (a NF-e segue a regra da filial).
        // Documento eletrônico de terceiro: chave coerente com o fornecedor, número e série (spec terceiro-chave §7).
        if (SupplierNfeKeyGuard.AppliesTo(existing) &&
            (existing.TaxDocumentKind == TaxDocumentKind.Cte || await taxApply.IsBranchActiveAsync(existing.BranchCode)))
            SupplierNfeKeyGuard.Ensure(existing, (await businessPartnerService.GetByIdAsync(existing.CardCode))?.TaxId);

        // A consulta à SEFAZ vale para a chave, o tipo e o emitente de quando foi feita (spec terceiro-chave §8): se
        // algum mudou, o protocolo gravado é de outra nota e o confirmar precisa consultar de novo.
        if ((NormalizeKey(existing.ChaveNFe), existing.TaxDocumentKind, existing.IssuerType) != storedAuthorizationScope)
        {
            existing.SupplierNfeProtocol = null;
            existing.SupplierNfeCheckedAt = null;
        }

        await PurchaseInvoiceChaveNFe.EnsureFreeAsync(db, existing.ChaveNFe, existing.Key);

        await SyncItemsAsync(existing, entity);

        // A natureza decide se a linha exige ou recusa o contrato, sobre a lista final (spec 2026-10-17 §1).
        await taxApply.EnsureUsageContractRuleAsync(existing, existing.Items.ToList());

        PurchaseInvoiceLineGuard.EnsureUnitOfMeasure(existing, existing.Items.ToList());

        // Spec 2026-10-13 D11 (o tipo é editável enquanto Pendente: a regra vale sobre a lista final).
        await taxApply.EnsureComplementAsync(existing, existing.Items.ToList());

        // Retorno de depósito sobre a lista final de linhas (mantidas/alteradas + novas), sem as removidas (spec 2026-10-10 §3.3).
        await StorageDepositReturnGuard.EnsureAsync(db.Context, existing.CardCode, existing.Key, existing.Items.ToList());
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, existing, existing.Items.ToList());

        // CT-e (spec 2026-10-17): documento de terceiro Normal, uma linha, UFs e tomador coerentes com a filial.
        if (PurchaseInvoiceCteRules.IsCte(existing))
            await PurchaseInvoiceCteRules.EnsureAsync(db.Context, existing, existing.Items.ToList());

        // Só na filial que emite NF-e pelo Siagro (regra ativa); fora dela o documento grava como veio.
        if (await taxApply.IsBranchActiveAsync(existing.BranchCode))
            PurchaseInvoiceSupplierItemNumbers.Ensure(existing, existing.Items);

        if (existing.IsNfeReturn)
            await PurchaseInvoiceNfeReturnBalance.EnsureWithinAsync(db.Context, existing, existing.Items);

        // O PATCH do cabeçalho traz todas as linhas (o controller carrega com Include e aplica o Delta),
        // então toda alteração passa por aqui: recalcula o documento inteiro — data, fornecedor e filial
        // mudam o CFOP e a alíquota de todas as linhas. As linhas incluídas no SyncItems já estão na
        // coleção pelo fixup do EF.
        // Tipo, emitente e filial são editáveis (só enquanto Pendente e sem NF-e emitida: travas acima). Se a troca tira o
        // documento da regra, o ApplyAsync sai cedo e a linha guardaria o Funrural calculado antes; fora da regra não há Funrural.
        if (!await taxApply.IsActiveForAsync(existing))
            foreach (var item in existing.Items)
                PurchaseInvoiceFunrural.Clear(item);

        await taxApply.ApplyAsync(existing, existing.Items);

        // Retenções de serviço (spec 2026-10-17 §2): os valores e o "manual" que o cliente mandou, copiados acima, valem
        // enquanto o usuário os editou; senão são recalculados pela natureza.
        await taxApply.ApplyWithholdingsAsync(existing);
        await taxApply.ApplyDifalAsync(existing);

        // Valor declarado do terceiro Normal: a soma das linhas, como na emissão própria (o da tela não vale).
        if (PurchaseInvoiceDeclaredTotal.AppliesTo(existing) && await taxApply.IsBranchActiveAsync(existing.BranchCode))
            PurchaseInvoiceDeclaredTotal.Apply(existing, existing.Items);

        await db.SaveChangesAsync();
    }

    /// <summary>Chave sem espaços (a colada do DANFE vem em grupos de 4); em branco e nula são a mesma coisa.</summary>
    private static string NormalizeKey(string? key) => string.Concat((key ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));

    /// <summary>
    /// Reconcilia a coleção pela chave da linha: casa atualiza, sobra no entrante insere, sobra no
    /// existente remove.
    ///
    /// Reconciliar em vez de limpar-e-recriar preserva o <c>RowId</c> e a identidade das linhas que
    /// só mudaram de amarração — que é o caso mais comum aqui.
    ///
    /// A remoção é feita no DbSet, e NÃO só na coleção: <c>PurchaseInvoiceKey</c> é nulável, então
    /// a relação é opcional e o EF trataria a saída da coleção como órfã (FK nula) em vez de
    /// exclusão. A linha sumiria do Include e continuaria na tabela.
    ///
    /// A descrição do produto é RE-RESOLVIDA, e não copiada, pelo mesmo motivo do nome do emitente
    /// acima: o value help a grava com group ID null e o PATCH chega com a descrição do produto
    /// ANTERIOR (ou em branco, numa linha nova). Ver <c>PurchaseInvoiceLineGuard</c>.
    /// </summary>
    private async Task SyncItemsAsync(PurchaseInvoice existing, PurchaseInvoice entity)
    {
        var incoming = entity.Items.ToList();

        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5): antes de qualquer cópia.
        foreach (var line in incoming)
            InvoiceLineChargeRules.Ensure(line);

        var removed = existing.Items
            .Where(current => incoming.All(i => i.Key != current.Key))
            .ToList();

        if (removed.Count > 0)
        {
            PurchaseInvoiceNfeLock.EnsureLinesRemovable(existing);
            await PurchaseInvoiceCounterNote.EnsureLinesNotReferencedAsync(db.Context, removed);
        }

        foreach (var line in removed)
        {
            existing.Items.Remove(line);
            db.Context.PurchaseInvoicesItems.Remove(line);
        }

        var typedIcms = existing.InvoiceType == PurchaseInvoiceType.IcmsComplement;

        foreach (var line in incoming)
        {
            var current = line.Key is null
                ? null
                : existing.Items.FirstOrDefault(x => x.Key == line.Key);

            if (current is null)
            {
                PurchaseInvoiceNfeLock.EnsureLineCanBeAdded(existing);

                // Complemento próprio: a regra do complemento valida o contrato depois de C3 (spec 2026-10-14, D5).
                if (line.ComplementedItemKey is null)
                    await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                        db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

                // Add no DbSet, e NÃO em existing.Items: Key é ValueGeneratedOnAdd, e uma entidade
                // com chave já preenchida entrando pela NAVEGAÇÃO é lida pelo EF como registro
                // existente sendo reanexado — vira UPDATE de linha inexistente. O Add explícito
                // força o estado Added; a coleção do pai é preenchida pelo fixup.
                var newLine = new PurchaseInvoiceItem
                {
                    Key = line.Key ?? Guid.NewGuid(),
                    PurchaseInvoiceKey = existing.Key,
                    ItemCode = line.ItemCode,
                    ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
                        itemService, line.ItemCode, line.ItemName,
                        otherPurchase: await taxApply.IsOtherPurchaseLineAsync(existing, line)),
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    UnitOfMeasureCode = line.UnitOfMeasureCode,
                    SalesInvoiceItemKey = line.SalesInvoiceItemKey,
                    PurchaseInvoiceItemOriginKey = line.PurchaseInvoiceItemOriginKey,
                    PurchaseContractKey = line.PurchaseContractKey,
                    UsageCode = line.UsageCode,
                    FreightValue = line.FreightValue,
                    InsuranceValue = line.InsuranceValue,
                    DiscountValue = line.DiscountValue,
                    OtherExpensesValue = line.OtherExpensesValue,
                    ComplementAmount = line.ComplementAmount,
                    ComplementedItemKey = line.ComplementedItemKey,
                    // Complemento de ICMS (spec 2026-10-17 D4): base e valor são informados; nos demais o motor calcula.
                    IcmsBase = typedIcms ? line.IcmsBase : 0m,
                    IcmsValue = typedIcms ? line.IcmsValue : 0m,
                };

                // Dimensões gerenciais da linha nova: o corpo, completado pelo cabeçalho, fornecedor e natureza.
                DimensionSnapshot.Copy(line, newLine);
                await DimensionWiring.ApplyAsync(dimensionDefaults, dimensionValidator, newLine, existing,
                    existing.CardCode, newLine.UsageCode, previous: null);

                db.Context.PurchaseInvoicesItems.Add(newLine);

                continue;
            }

            // Complemento próprio: a regra do complemento valida o contrato DEPOIS de C3 — trocar o produtor do cabeçalho
            // recusa por C3, e o contrato escolhido pelo operador continua passando pela guarda (spec 2026-10-14, D5).
            if (line.ComplementedItemKey is null)
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);

            // Capturado ANTES da atribuição: depois dela a comparação é sempre falsa.
            var itemCodeChanged = current.ItemCode != line.ItemCode;

            // Linha existente: valida só o que mudou e NÃO aplica padrões — mudar o cabeçalho não reescreve as linhas.
            var previousLineDims = DimensionSnapshot.FromOriginal(db.Context.Entry(current).OriginalValues);
            DimensionSnapshot.Copy(line, current);
            await DimensionWiring.ApplyAsync(dimensionDefaults, dimensionValidator, current, existing,
                existing.CardCode, current.UsageCode, previousLineDims, applyDefaults: false);

            current.ItemCode = line.ItemCode;
            current.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
                itemService, line.ItemCode, line.ItemName, itemCodeChanged,
                otherPurchase: await taxApply.IsOtherPurchaseLineAsync(existing, line));
            current.Quantity = line.Quantity;
            current.UnitPrice = line.UnitPrice;
            current.UnitOfMeasureCode = line.UnitOfMeasureCode;
            current.SalesInvoiceItemKey = line.SalesInvoiceItemKey;
            current.PurchaseInvoiceItemOriginKey = line.PurchaseInvoiceItemOriginKey;
            current.PurchaseContractKey = line.PurchaseContractKey;
            current.UsageCode = line.UsageCode;
            current.FreightValue = line.FreightValue;
            current.InsuranceValue = line.InsuranceValue;
            current.DiscountValue = line.DiscountValue;
            current.OtherExpensesValue = line.OtherExpensesValue;
            current.ComplementAmount = line.ComplementAmount;
            current.ComplementedItemKey = line.ComplementedItemKey;
            if (typedIcms)
            {
                current.IcmsBase = line.IcmsBase;
                current.IcmsValue = line.IcmsValue;
            }

            // Na devolução de compra só a quantidade e os quatro valores da linha mudam (a descrição re-resolvida também volta).
            var lineEntry = db.Context.Entry(current);
            if (existing.IsNfeReturn)
            {
                PurchaseInvoiceNfeLock.RestoreReturnLine(lineEntry);
                // Revalida com o preço/produto que a trava restaurou: o Ensure do início viu os valores do corpo.
                InvoiceLineChargeRules.Ensure(current);
            }

            // Contra-nota: a linha é a da escrituração (spec 2026-10-10 §7).
            if (existing.ProducerInvoiceKey is not null)
            {
                PurchaseInvoiceNfeLock.RestoreCounterNoteLine(lineEntry);
                InvoiceLineChargeRules.Ensure(current);
            }
            PurchaseInvoiceNfeLock.EnsureItemEditable(existing.NfeStatus, lineEntry);

            if (existing.IsNfeReturn && current.Quantity <= 0)
                throw new DefaultException($"Item {current.ItemCode}: informe a quantidade a devolver.");
        }
    }
}
