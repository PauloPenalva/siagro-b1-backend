#!/usr/bin/env python3
"""Confere os tributos de NF-e (modelo 55) a partir do XML.

Recalcula, item a item, ICMS (inclusive redução de base, diferimento, ST e FCP),
IPI, PIS/COFINS e IBS/CBS (fase de teste de 2026), e confere os totais do grupo
<total> contra a soma dos itens. Também confere a coerência dos CSTs e faz alertas
específicos de cereais (diferimento, suspensão de PIS/COFINS, Funrural).

Uso:
    python conferir_nfe.py <arquivo.xml|pasta> [...] [--json] [--tolerancia 0.01]

Saída: relatório em Markdown (ou JSON com --json). Código de saída 1 quando há
alguma divergência de nível ERRO, 0 caso contrário.
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field, asdict
from decimal import Decimal, ROUND_HALF_UP, ROUND_HALF_EVEN
from pathlib import Path

ZERO = Decimal("0")
CENT = Decimal("0.01")
HUNDRED = Decimal("100")

# NCMs de cereais/oleaginosas mais comuns no Siagro (prefixos de 4 dígitos).
GRAIN_NCM_PREFIXES = ("1001", "1003", "1005", "1006", "1007", "1201", "1202", "1206", "1207", "1208", "2304")

# CST de PIS/COFINS sem valor de tributo.
PISCOFINS_NO_VALUE_CSTS = {"04", "05", "06", "07", "08", "09"}
# CST de PIS/COFINS tributados com alíquota (exigem valor > 0).
PISCOFINS_TAXED_CSTS = {"01", "02"}

Tolerance = Decimal


@dataclass
class Finding:
    level: str  # ERRO | ALERTA | INFO
    item: str  # nItem ou "total"/"nota"
    field: str
    message: str
    expected: str = ""
    found: str = ""


@dataclass
class Report:
    file: str
    key: str = ""
    number: str = ""
    series: str = ""
    nat_op: str = ""
    issuer: str = ""
    issuer_uf: str = ""
    recipient_uf: str = ""
    entry_exit: str = ""
    items: int = 0
    findings: list[Finding] = field(default_factory=list)

    def add(self, level, item, fld, message, expected="", found=""):
        self.findings.append(Finding(level, str(item), fld, message, str(expected), str(found)))

    @property
    def errors(self):
        return [f for f in self.findings if f.level == "ERRO"]


# ---------------------------------------------------------------- XML helpers

def strip_ns(root: ET.Element) -> ET.Element:
    for el in root.iter():
        if isinstance(el.tag, str) and "}" in el.tag:
            el.tag = el.tag.split("}", 1)[1]
    return root


def dec(el: ET.Element | None, path: str) -> Decimal | None:
    if el is None:
        return None
    node = el.find(path)
    if node is None or node.text is None or node.text.strip() == "":
        return None
    return Decimal(node.text.strip())


def dz(el, path) -> Decimal:
    v = dec(el, path)
    return v if v is not None else ZERO


def txt(el: ET.Element | None, path: str) -> str:
    if el is None:
        return ""
    node = el.find(path)
    return (node.text or "").strip() if node is not None else ""


def r2(v: Decimal) -> Decimal:
    return v.quantize(CENT, rounding=ROUND_HALF_UP)


def pct(base: Decimal, rate: Decimal) -> Decimal:
    return base * rate / HUNDRED


class Checker:
    def __init__(self, report: Report, tol: Decimal):
        self.r = report
        self.tol = tol

    def eq(self, item, fld, expected: Decimal, found: Decimal | None, msg: str, level="ERRO"):
        """Compara valor esperado (antes do arredondamento) com o do XML.

        Aceita arredondamento half-up, half-even e truncamento dentro da tolerância,
        porque emissores diferentes arredondam de forma diferente.
        """
        if found is None:
            if r2(expected) != ZERO:
                self.r.add(level, item, fld, f"{msg}: campo ausente no XML", r2(expected), "(ausente)")
            return
        candidates = {
            expected.quantize(CENT, rounding=ROUND_HALF_UP),
            expected.quantize(CENT, rounding=ROUND_HALF_EVEN),
        }
        if any(abs(c - found) <= self.tol for c in candidates):
            return
        self.r.add(level, item, fld, msg, r2(expected), found)


# ---------------------------------------------------------------- item checks

def item_operation_base(prod: ET.Element) -> Decimal:
    """Base típica de ICMS: produto + frete + seguro + outras despesas - desconto."""
    return dz(prod, "vProd") + dz(prod, "vFrete") + dz(prod, "vSeg") + dz(prod, "vOutro") - dz(prod, "vDesc")


SOUTH_SOUTHEAST_EXCEPT_ES = {"PR", "SC", "RS", "SP", "RJ", "MG"}


def interstate_rate(orig_uf: str, dest_uf: str, goods_origin: str) -> Decimal:
    if goods_origin in ("1", "2", "3", "8"):
        return Decimal("4")
    if orig_uf in SOUTH_SOUTHEAST_EXCEPT_ES and dest_uf not in SOUTH_SOUTHEAST_EXCEPT_ES:
        return Decimal("7")
    return Decimal("12")


def check_prod(c: Checker, n: str, prod: ET.Element):
    q, vu, vprod = dec(prod, "qCom"), dec(prod, "vUnCom"), dec(prod, "vProd")
    if q is not None and vu is not None and vprod is not None:
        c.eq(n, "vProd", q * vu, vprod, "vProd diferente de qCom x vUnCom")


def check_icms(c: Checker, n: str, prod: ET.Element, imposto: ET.Element, sums: dict):
    icms = imposto.find("ICMS")
    if icms is None or len(icms) == 0:
        return
    g = icms[0]
    grp = g.tag  # ICMS00, ICMS20, ICMS51, ICMSSN101...
    cst = txt(g, "CST") or txt(g, "CSOSN")
    sums["groups"].append(f"{grp}/{cst}")
    op_base = item_operation_base(prod)
    ipi_val = dz(imposto, "IPI/IPITrib/vIPI")

    vbc, picms, vicms = dec(g, "vBC"), dec(g, "pICMS"), dec(g, "vICMS")
    pred = dz(g, "pRedBC")

    if vbc is not None:
        sums["vBC"] += vbc
    if grp == "ICMS51" and vbc == ZERO and (vicms or ZERO) == ZERO and op_base > ZERO:
        c.r.add("INFO", n, "ICMS51.vBC", "Diferimento com base zerada (vBC=0) — prática de alguns emissores; "
                "o Siagro informa a base cheia com vICMSOp e vICMSDif")
    elif vbc is not None and not sums["complementar"]:
        expected_base = op_base * (HUNDRED - pred) / HUNDRED
        expected_base_ipi = (op_base + ipi_val) * (HUNDRED - pred) / HUNDRED
        if not any(abs(r2(b) - vbc) <= c.tol for b in (expected_base, expected_base_ipi)):
            c.r.add("ALERTA", n, f"{grp}.vBC",
                    "Base de ICMS diferente de (vProd+frete+seguro+outras-desconto) com a redução informada"
                    + (f" (pRedBC={pred}%)" if pred else ""), r2(expected_base), vbc)
        elif ipi_val and abs(r2(expected_base_ipi) - vbc) <= c.tol and abs(r2(expected_base) - vbc) > c.tol:
            c.r.add("INFO", n, f"{grp}.vBC", "IPI incluído na base do ICMS (correto só quando o destinatário é consumidor final/uso e consumo)")

    # Alíquota interestadual (Res. SF 22/89 e 13/2012) — mesma tabela de Fiscal/Taxes/InterstateIcmsRate.cs
    o_uf, d_uf = sums["uf_orig"], sums["uf_dest"]
    if picms is not None and o_uf and d_uf and o_uf != d_uf and d_uf != "EX" and txt(prod, "CFOP")[:1] in ("2", "6"):
        exp_rate = interstate_rate(o_uf, d_uf, txt(g, "orig"))
        if picms != exp_rate:
            c.r.add("ERRO", n, f"{grp}.pICMS", f"Alíquota interestadual {o_uf}→{d_uf} (origem {txt(g, 'orig')}) deveria ser {exp_rate}%",
                    exp_rate, picms)

    if grp == "ICMS51":
        vop, pdif, vdif = dec(g, "vICMSOp"), dz(g, "pDif"), dec(g, "vICMSDif")
        if vbc is not None and picms is not None:
            exp_op = pct(vbc, picms)
            c.eq(n, "ICMS51.vICMSOp", exp_op, vop, "vICMSOp diferente de vBC x pICMS")
            op = vop if vop is not None else r2(exp_op)
            exp_dif = pct(op, pdif)
            c.eq(n, "ICMS51.vICMSDif", exp_dif, vdif, "vICMSDif diferente de vICMSOp x pDif")
            dif = vdif if vdif is not None else r2(exp_dif)
            c.eq(n, "ICMS51.vICMS", op - dif, vicms, "vICMS diferente de vICMSOp - vICMSDif")
            sums["icms_exp"] = r2(exp_op) - r2(pct(r2(exp_op), pdif))
        if pdif == HUNDRED and not txt(prod, "cBenef"):
            c.r.add("ALERTA", n, "cBenef", "Diferimento total (CST 51, pDif=100%) sem código de benefício fiscal (cBenef) — várias UFs (ex.: SP) exigem")
    elif vbc is not None and picms is not None:
        c.eq(n, f"{grp}.vICMS", pct(vbc, picms), vicms, "vICMS diferente de vBC x pICMS")
        sums["icms_exp"] = r2(pct(vbc, picms))

    if vicms is not None and grp not in ("ICMS60",):
        sums["vICMS"] += vicms

    # Desoneração
    vdeson = dec(g, "vICMSDeson")
    if vdeson is not None:
        sums["vICMSDeson"] += vdeson
        if not txt(g, "motDesICMS"):
            c.r.add("ERRO", n, f"{grp}.motDesICMS", "vICMSDeson informado sem motivo da desoneração")

    # FCP
    pfcp, vfcp = dec(g, "pFCP"), dec(g, "vFCP")
    if pfcp is not None:
        base_fcp = dec(g, "vBCFCP") or vbc or ZERO
        c.eq(n, f"{grp}.vFCP", pct(base_fcp, pfcp), vfcp, "vFCP diferente de base x pFCP")
    if vfcp is not None:
        sums["vFCP"] += vfcp

    # ST
    vbcst, picmsst, vicmsst = dec(g, "vBCST"), dec(g, "pICMSST"), dec(g, "vICMSST")
    if vbcst is not None:
        sums["vBCST"] += vbcst
        mva, predst = dz(g, "pMVAST"), dz(g, "pRedBCST")
        if txt(g, "modBCST") == "4":  # MVA
            exp_bcst = (op_base + ipi_val) * (HUNDRED + mva) / HUNDRED * (HUNDRED - predst) / HUNDRED
            c.eq(n, f"{grp}.vBCST", exp_bcst, vbcst,
                 "vBCST diferente de (base + IPI) x (1+MVA) x (1-redução ST)", level="ALERTA")
        if picmsst is not None:
            own = vicms if vicms is not None else (dz(g, "vICMSOp") if grp == "ICMS51" else ZERO)
            if grp in ("ICMS30",):
                own = pct(op_base, picms or ZERO) if picms is not None else own
            c.eq(n, f"{grp}.vICMSST", pct(vbcst, picmsst) - own, vicmsst,
                 "vICMSST diferente de vBCST x pICMSST - ICMS próprio")
    if vicmsst is not None:
        sums["vST"] += vicmsst
    vfcpst = dec(g, "vFCPST")
    if vfcpst is not None:
        sums["vFCPST"] += vfcpst

    # Coerência de CST
    if cst in ("40", "41", "50") and (vicms or ZERO) != ZERO:
        c.r.add("ERRO", n, f"{grp}.vICMS", f"CST {cst} (isenta/não tributada/suspensão) com ICMS destacado", "0.00", vicms)
    if cst == "00" and pred:
        c.r.add("ERRO", n, f"{grp}.pRedBC", "CST 00 não admite redução de base — usar CST 20")
    if cst == "20" and not pred:
        c.r.add("ERRO", n, f"{grp}.pRedBC", "CST 20 sem percentual de redução de base")


def item_icms(imposto: ET.Element, sums: dict) -> Decimal:
    if sums.get("icms_exp") is not None:
        return sums["icms_exp"]
    icms_el = imposto.find("ICMS")
    return dz(icms_el[0], "vICMS") if icms_el is not None and len(icms_el) else ZERO


def check_ipi(c: Checker, n: str, imposto: ET.Element, sums: dict):
    trib = imposto.find("IPI/IPITrib")
    if trib is None:
        return
    vipi = dec(trib, "vIPI")
    if dec(trib, "pIPI") is not None:
        c.eq(n, "IPI.vIPI", pct(dz(trib, "vBC"), dz(trib, "pIPI")), vipi, "vIPI diferente de vBC x pIPI")
    elif dec(trib, "vUnid") is not None:
        c.eq(n, "IPI.vIPI", dz(trib, "qUnid") * dz(trib, "vUnid"), vipi, "vIPI diferente de qUnid x vUnid")
    sums["vIPI"] += vipi or ZERO


def check_piscofins(c: Checker, n: str, prod: ET.Element, imposto: ET.Element, sums: dict, tax: str):
    root = imposto.find(tax)
    if root is None or len(root) == 0:
        return
    g = root[0]
    cst = txt(g, "CST")
    p = tax  # "PIS" ou "COFINS"
    vbc, rate, val = dec(g, "vBC"), dec(g, f"p{p}"), dec(g, f"v{p}")
    if vbc is not None and rate is not None:
        c.eq(n, f"{p}.v{p}", pct(vbc, rate), val, f"v{p} diferente de vBC x p{p}")
    elif dec(g, "qBCProd") is not None:
        c.eq(n, f"{p}.v{p}", dz(g, "qBCProd") * dz(g, "vAliqProd"), val, f"v{p} diferente de qBCProd x vAliqProd")
    sums[f"v{p}"] += val or ZERO
    if vbc is not None and rate is not None:
        sums[f"{p.lower()}_exp"] = r2(pct(vbc, rate))
    elif val is not None:
        sums[f"{p.lower()}_exp"] = val

    if vbc is not None and not sums["complementar"]:
        # ICMS "correto" do item (recalculado): se o vICMS do XML estiver errado, isso já foi
        # apontado — a base de PIS/COFINS é julgada contra o ICMS que deveria estar ali.
        vicms = item_icms(imposto, sums)
        op_base = dz(prod, "vProd") + dz(prod, "vFrete") + dz(prod, "vSeg") + dz(prod, "vOutro") - dz(prod, "vDesc")
        sums.setdefault(f"{p}_base_mode", set())
        if abs(vbc - r2(op_base - vicms)) <= c.tol and vicms:
            sums[f"{p}_base_mode"].add("exclui ICMS")
        elif abs(vbc - r2(op_base)) <= c.tol:
            sums[f"{p}_base_mode"].add("inclui ICMS" if vicms else "valor da operação")
        else:
            sums[f"{p}_base_mode"].add("outra")
            c.r.add("ALERTA", n, f"{p}.vBC",
                    f"Base de {p} não bate nem com a operação nem com operação - ICMS (Tema 69 STF)",
                    f"{r2(op_base)} ou {r2(op_base - vicms)}", vbc)

    if cst in PISCOFINS_TAXED_CSTS and (val or ZERO) == ZERO:
        c.r.add("ERRO", n, f"{p}.CST", f"CST {cst} (tributado) com v{p} zerado")
    if cst in PISCOFINS_NO_VALUE_CSTS and (val or ZERO) != ZERO:
        c.r.add("ERRO", n, f"{p}.CST", f"CST {cst} (sem incidência/isento/suspenso) com v{p} destacado", "0.00", val)
    sums.setdefault(f"{p}_csts", set()).add(cst)


def check_ibscbs(c: Checker, n: str, prod: ET.Element, imposto: ET.Element, sums: dict):
    root = imposto.find("IBSCBS")
    if root is None:
        sums["sem_ibscbs"] += 1
        return
    cst = txt(root, "CST")
    g = root.find("gIBSCBS")
    if g is None:
        return
    vbc = dz(g, "vBC")
    sums["vBCIBSCBS"] += vbc

    # Base 2026: operação - ICMS - PIS - COFINS (LC 214/2025, art. 12 c/c regras de transição)
    op_base = item_operation_base(prod)
    icms_el = imposto.find("ICMS")
    vicms = item_icms(imposto, sums)
    vpis, vcof = sums["pis_exp"], sums["cofins_exp"]
    exp_base = op_base - vicms - vpis - vcof
    if not sums["complementar"]:
        c.eq(n, "IBSCBS.vBC", exp_base, vbc,
             "Base de IBS/CBS diferente de operação - ICMS - PIS - COFINS", level="ALERTA")

    def part(path, rate_tag, val_tag, key):
        el = g.find(path)
        if el is None:
            return ZERO
        rate = dz(el, rate_tag)
        red = el.find("gRed")
        eff = rate
        if red is not None:
            pred = dz(red, "pRedAliq")
            eff_xml = dec(red, "pAliqEfet")
            eff = rate * (HUNDRED - pred) / HUNDRED
            if eff_xml is not None and abs(eff - eff_xml) > Decimal("0.0001"):
                c.r.add("ERRO", n, f"IBSCBS.{path}.pAliqEfet", "Alíquota efetiva diferente de alíquota x (1 - pRedAliq)", eff, eff_xml)
                eff = eff_xml
        val = dec(el, val_tag)
        c.eq(n, f"IBSCBS.{path}.{val_tag}", pct(vbc, eff), val, f"{val_tag} diferente de vBC x alíquota efetiva")
        sums[key] += val or ZERO
        return val or ZERO

    uf = part("gIBSUF", "pIBSUF", "vIBSUF", "vIBSUF")
    mun = part("gIBSMun", "pIBSMun", "vIBSMun", "vIBSMun")
    part("gCBS", "pCBS", "vCBS", "vCBS")
    c.eq(n, "IBSCBS.vIBS", uf + mun, dec(g, "vIBS"), "vIBS diferente de vIBSUF + vIBSMun")
    sums["vIBS"] += dz(g, "vIBS")

    if cst == "000" and g.find("gIBSUF/gRed") is not None:
        c.r.add("ERRO", n, "IBSCBS.CST", "CST 000 (tributação integral) com redução de alíquota — usar CST 200")


def agro_checks(c: Checker, n: str, prod: ET.Element, imposto: ET.Element, ide: ET.Element):
    ncm = txt(prod, "NCM")
    if not ncm.startswith(GRAIN_NCM_PREFIXES):
        return
    cfop = txt(prod, "CFOP")
    icms = imposto.find("ICMS")
    cst = ""
    if icms is not None and len(icms):
        cst = txt(icms[0], "CST")
    pis_cst = txt(imposto, "PIS/*/CST")
    # Entrada de cereal comprado de produtor/cooperativa para industrialização/revenda:
    # PIS/COFINS normalmente suspensos (Lei 10.925/2004 art. 9º) -> CST 74 (entrada) / 09 (saída do produtor)
    if cfop.startswith("1") or cfop.startswith("2"):
        if pis_cst in PISCOFINS_TAXED_CSTS:
            c.r.add("ALERTA", n, "PIS.CST",
                    f"Entrada de cereal (NCM {ncm}) com PIS/COFINS tributado (CST {pis_cst}) — verifique se cabe suspensão "
                    "(Lei 10.925/2004, art. 9º; CST 74 na entrada)")
    if cst == "00" and txt(ide, "idDest") == "1" and cfop[1:] in ("101", "102"):
        c.r.add("INFO", n, "ICMS.CST",
                f"Operação interna com cereal (NCM {ncm}) totalmente tributada — várias UFs diferem o ICMS nessas "
                "operações (CST 51). Confirme a regra da UF.")


# ---------------------------------------------------------------- totals

def check_totals(c: Checker, total: ET.Element | None, sums: dict, prods: list[ET.Element], has_ibscbs_items: bool):
    if total is None:
        c.r.add("ERRO", "total", "total", "Grupo <total> ausente")
        return
    t = total.find("ICMSTot")
    sum_prod = lambda tag: sum((dz(p, tag) for p in prods if txt(p, "indTot") != "0"), ZERO)
    pairs = [
        ("vBC", sums["vBC"]), ("vICMS", sums["vICMS"]), ("vICMSDeson", sums["vICMSDeson"]),
        ("vFCP", sums["vFCP"]), ("vBCST", sums["vBCST"]), ("vST", sums["vST"]), ("vFCPST", sums["vFCPST"]),
        ("vProd", sum_prod("vProd")), ("vFrete", sum_prod("vFrete")), ("vSeg", sum_prod("vSeg")),
        ("vDesc", sum_prod("vDesc")), ("vOutro", sum_prod("vOutro")), ("vIPI", sums["vIPI"]),
        ("vPIS", sums["vPIS"]), ("vCOFINS", sums["vCOFINS"]),
    ]
    for tag, expected in pairs:
        found = dec(t, tag)
        if found is None and expected == ZERO:
            continue
        c.eq("total", f"ICMSTot.{tag}", expected, found, f"Total {tag} diferente da soma dos itens")

    exp_vnf = (dz(t, "vProd") - dz(t, "vDesc") - dz(t, "vICMSDeson") + dz(t, "vST") + dz(t, "vFCPST")
               + dz(t, "vFrete") + dz(t, "vSeg") + dz(t, "vOutro") + dz(t, "vII") + dz(t, "vIPI")
               + dz(t, "vIPIDevol") + dz(t, "vServ"))
    c.eq("total", "ICMSTot.vNF", exp_vnf, dec(t, "vNF"),
         "vNF diferente de vProd - vDesc - vICMSDeson + vST + vFCPST + vFrete + vSeg + vOutro + vII + vIPI + vIPIDevol")

    ib = total.find("IBSCBSTot")
    if ib is None:
        if has_ibscbs_items:
            c.r.add("ERRO", "total", "IBSCBSTot", "Itens com IBS/CBS mas sem grupo IBSCBSTot nos totais")
        return
    for path, key in [("vBCIBSCBS", "vBCIBSCBS"), ("gIBS/gIBSUF/vIBSUF", "vIBSUF"),
                      ("gIBS/gIBSMun/vIBSMun", "vIBSMun"), ("gIBS/vIBS", "vIBS"), ("gCBS/vCBS", "vCBS")]:
        c.eq("total", f"IBSCBSTot.{path}", sums[key], dec(ib, path), f"Total {path} diferente da soma dos itens")


# ---------------------------------------------------------------- main

def check_file(path: Path, tol: Decimal) -> Report | None:
    root = strip_ns(ET.parse(path).getroot())
    inf = root.find(".//infNFe") if root.tag in ("nfeProc", "NFe") else None
    if inf is None:
        return None  # evento, CT-e etc.
    ide, emit, dest = inf.find("ide"), inf.find("emit"), inf.find("dest")
    rep = Report(
        file=str(path),
        key=(inf.get("Id") or "").replace("NFe", ""),
        number=txt(ide, "nNF"), series=txt(ide, "serie"), nat_op=txt(ide, "natOp"),
        issuer=txt(emit, "xNome"), issuer_uf=txt(emit, "enderEmit/UF"),
        recipient_uf=txt(dest, "enderDest/UF"),
        entry_exit="entrada" if txt(ide, "tpNF") == "0" else "saída",
    )
    if txt(ide, "mod") and txt(ide, "mod") != "55":
        rep.add("INFO", "nota", "mod", f"Modelo {txt(ide, 'mod')} — o script foi pensado para NF-e modelo 55")
    if txt(emit, "CRT") == "1":
        rep.add("INFO", "nota", "CRT", "Emitente do Simples Nacional (CRT=1): ICMS por CSOSN, conferência de alíquota é limitada")

    c = Checker(rep, tol)
    sums = {k: ZERO for k in ("vBC", "vICMS", "vICMSDeson", "vFCP", "vBCST", "vST", "vFCPST", "vIPI", "vPIS",
                              "vCOFINS", "vBCIBSCBS", "vIBSUF", "vIBSMun", "vIBS", "vCBS")}
    sums["groups"] = []
    sums["sem_ibscbs"] = 0
    # Nota complementar/de ajuste: a base é informada à parte, não vem de vProd.
    sums["complementar"] = txt(ide, "finNFe") in ("2", "3")
    if sums["complementar"]:
        rep.add("INFO", "nota", "finNFe", "Nota complementar/de ajuste: bases não conferidas contra vProd, só a conta base × alíquota e os totais")
    # UF de origem/destino da mercadoria: na entrada própria (tpNF=0) o fornecedor é o destinatário do XML.
    if txt(ide, "tpNF") == "0":
        sums["uf_orig"], sums["uf_dest"] = rep.recipient_uf, rep.issuer_uf
    else:
        sums["uf_orig"], sums["uf_dest"] = rep.issuer_uf, rep.recipient_uf
    prods = []
    dets = inf.findall("det")
    rep.items = len(dets)
    for det in dets:
        n = det.get("nItem", "?")
        prod, imposto = det.find("prod"), det.find("imposto")
        prods.append(prod)
        sums["icms_exp"], sums["pis_exp"], sums["cofins_exp"] = None, ZERO, ZERO
        check_prod(c, n, prod)
        if imposto is None:
            rep.add("ERRO", n, "imposto", "Item sem grupo <imposto>")
            continue
        check_icms(c, n, prod, imposto, sums)
        check_ipi(c, n, imposto, sums)
        check_piscofins(c, n, prod, imposto, sums, "PIS")
        check_piscofins(c, n, prod, imposto, sums, "COFINS")
        check_ibscbs(c, n, prod, imposto, sums)
        agro_checks(c, n, prod, imposto, ide)

    for p in ("PIS", "COFINS"):
        modes = sums.get(f"{p}_base_mode", set())
        if len(modes - {"valor da operação"}) > 1:
            rep.add("ALERTA", "nota", f"{p}.vBC", f"Itens com critérios diferentes de base de {p}: {', '.join(sorted(modes))}")
        elif "exclui ICMS" in modes:
            rep.add("INFO", "nota", f"{p}.vBC", f"Base de {p} exclui o ICMS destacado (Tema 69 STF)")
    if sums["sem_ibscbs"] and sums["sem_ibscbs"] < len(dets):
        rep.add("ALERTA", "nota", "IBSCBS", f"{sums['sem_ibscbs']} item(ns) sem grupo IBSCBS enquanto outros têm")
    elif sums["sem_ibscbs"] == len(dets) and txt(emit, "CRT") != "1":
        rep.add("INFO", "nota", "IBSCBS", "Nenhum item com grupo IBSCBS (em 2026 o destaque de teste é esperado para regime normal)")

    check_totals(c, inf.find("total"), sums, prods, sums["sem_ibscbs"] < len(dets))

    infcpl = (txt(inf, "infAdic/infCpl") + " " + txt(inf, "infAdic/infAdFisco")).upper()
    if "FUNRURAL" in infcpl or "SENAR" in infcpl or "GILRAT" in infcpl or "RAT " in infcpl:
        rep.add("INFO", "nota", "infCpl", "Informações complementares citam Funrural/Senar/GILRAT — confira com o cálculo do backend "
                "(a retenção não tem campo próprio no XML da NF-e)")
    return rep


def iter_files(args):
    for a in args:
        p = Path(a)
        if p.is_dir():
            yield from sorted(p.rglob("*.xml"))
        else:
            yield p


def to_markdown(reports: list[Report]) -> str:
    out = []
    for r in reports:
        status = "❌ com divergências" if r.errors else ("⚠️ com alertas" if any(f.level == "ALERTA" for f in r.findings) else "✅ ok")
        out.append(f"## NF-e {r.number}/{r.series} — {status}")
        out.append(f"- Arquivo: `{r.file}`")
        out.append(f"- Chave: {r.key}")
        out.append(f"- {r.entry_exit} · {r.nat_op} · emitente {r.issuer} ({r.issuer_uf}) → UF dest. {r.recipient_uf or '-'} · {r.items} item(ns)")
        if r.findings:
            out.append("")
            out.append("| Nível | Item | Campo | Problema | Esperado | No XML |")
            out.append("|---|---|---|---|---|---|")
            order = {"ERRO": 0, "ALERTA": 1, "INFO": 2}
            for f in sorted(r.findings, key=lambda f: order[f.level]):
                out.append(f"| {f.level} | {f.item} | {f.field} | {f.message} | {f.expected} | {f.found} |")
        out.append("")
    return "\n".join(out)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--tolerancia", default="0.01")
    a = ap.parse_args(argv)
    tol = Decimal(a.tolerancia)
    reports, skipped = [], []
    for f in iter_files(a.paths):
        try:
            rep = check_file(f, tol)
        except ET.ParseError as e:
            skipped.append(f"{f}: XML inválido ({e})")
            continue
        if rep is None:
            skipped.append(f"{f}: não é NF-e (evento, CT-e...)")
        else:
            reports.append(rep)
    if a.json:
        print(json.dumps({"reports": [asdict(r) for r in reports], "skipped": skipped}, ensure_ascii=False, indent=2))
    else:
        print(to_markdown(reports))
        if skipped:
            print("Ignorados:\n" + "\n".join(f"- {s}" for s in skipped))
    return 1 if any(r.errors for r in reports) else 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
