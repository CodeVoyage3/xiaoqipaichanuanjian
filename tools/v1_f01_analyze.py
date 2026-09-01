"""Read-only, repeatable aggregate profile and cold-start simulation for V1-F01.

Usage:
  python tools/v1_f01_analyze.py --source <xlsx> --output obj/V1F01/summary.json
"""
import argparse
import collections
import datetime as dt
import hashlib
import json
import math
from pathlib import Path

import openpyxl

BUSINESS_DATE = dt.date(2026, 9, 1)
REQUIRED = ("商品大类", "商品编码", "商品条码", "商品名称", "生产日期", "有效日期", "保质期", "保质期单位", "是否该做临期折扣", "该批次累计到货数量", "该商品门店库存总数")
SCHEMES = {
    "ratio_3pct": (0.03, None, None),
    "ratio_5pct": (0.05, None, None),
    "ratio_10pct": (0.10, None, None),
    "ratio_5pct_clamp_3_30": (0.05, 3, 30),
    "ratio_5pct_clamp_3_60": (0.05, 3, 60),
    "ratio_5pct_clamp_7_60": (0.05, 7, 60),
}


def clean(value):
    return None if value is None else str(value).strip() or None


def date_value(value):
    if isinstance(value, dt.datetime):
        return value.date()
    if isinstance(value, dt.date):
        return value
    return None


def integer(value):
    if isinstance(value, bool) or value is None:
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float) and value.is_integer():
        return int(value)
    text = clean(value)
    try:
        return int(text) if text and str(int(text)) == text else None
    except ValueError:
        return None


def stage(expiry, shelf_life, unit):
    total = shelf_life * {"D": 1, "M": 30, "Y": 365}[unit]
    remaining = (expiry - BUSINESS_DATE).days
    first, second, third = (90, 60, 14) if total > 270 else (30, 14, 7)
    if remaining <= 0:
        return "expired"
    if remaining > first:
        return "none"
    if remaining > second:
        return "discount_50"
    if remaining > third:
        return "discount_20"
    return "withdraw"


def window(days, ratio, lower, upper):
    value = math.ceil(days * ratio)
    if lower is not None:
        value = max(value, lower)
    if upper is not None:
        value = min(value, upper)
    return value


def counter_dict(counter):
    return dict(sorted(counter.items(), key=lambda x: str(x[0])))


def main():
    args = argparse.ArgumentParser()
    args.add_argument("--source", required=True)
    args.add_argument("--output", required=True)
    ns = args.parse_args()
    source, output = Path(ns.source), Path(ns.output)
    before = hashlib.sha256(source.read_bytes()).hexdigest().upper()
    size = source.stat().st_size
    wb = openpyxl.load_workbook(source, read_only=True, data_only=True)
    formula_wb = openpyxl.load_workbook(source, read_only=True, data_only=False)
    sheets = []
    for ws in wb.worksheets:
        headers = [clean(cell.value) for cell in next(ws.iter_rows(min_row=1, max_row=1))]
        formulas = sum(1 for row in formula_wb[ws.title].iter_rows() for cell in row if isinstance(cell.value, str) and cell.value.startswith("="))
        sheets.append({"name": ws.title, "rows": ws.max_row, "columns": ws.max_column, "headers": headers, "formula_count": formulas})
    if len(wb.worksheets) != 1:
        raise SystemExit("Expected exactly one worksheet for the V1-F01 source.")
    ws = wb.active
    headers = sheets[0]["headers"]
    missing = [x for x in REQUIRED if x not in headers]
    if missing:
        raise SystemExit("Missing required headers: " + ", ".join(missing))
    col = {header: headers.index(header) for header in REQUIRED}
    rows = []
    for excel_row, values in enumerate(ws.iter_rows(min_row=2, values_only=True), start=2):
        record = {name: values[index] if index < len(values) else None for name, index in col.items()}
        record["row"] = excel_row
        rows.append(record)

    field_quality = {name: collections.Counter() for name in REQUIRED}
    category_rows, product_rows, barcode_rows, batch_rows = collections.defaultdict(list), collections.defaultdict(list), collections.defaultdict(list), collections.defaultdict(list)
    usable = []
    invalid_reasons = collections.Counter()
    for r in rows:
        category, code, barcode, name = clean(r["商品大类"]), clean(r["商品编码"]), clean(r["商品条码"]), clean(r["商品名称"])
        production, expiry = date_value(r["生产日期"]), date_value(r["有效日期"])
        life, unit, stock, arrival = integer(r["保质期"]), clean(r["保质期单位"]), integer(r["该商品门店库存总数"]), integer(r["该批次累计到货数量"])
        for field, value in (("商品大类", category), ("商品编码", code), ("商品条码", barcode), ("商品名称", name), ("生产日期", production), ("有效日期", expiry), ("保质期", life), ("保质期单位", unit), ("是否该做临期折扣", clean(r["是否该做临期折扣"])), ("该批次累计到货数量", arrival), ("该商品门店库存总数", stock)):
            field_quality[field]["missing" if value is None else "present"] += 1
        if r["生产日期"] is not None and production is None: field_quality["生产日期"]["invalid"] += 1
        if r["有效日期"] is not None and expiry is None: field_quality["有效日期"]["invalid"] += 1
        if r["保质期"] is not None and life is None: field_quality["保质期"]["invalid"] += 1
        if life is not None and life <= 0: field_quality["保质期"]["non_positive"] += 1
        if unit is not None and unit not in {"D", "M", "Y"}: field_quality["保质期单位"]["invalid"] += 1
        if stock is not None and stock < 0: field_quality["该商品门店库存总数"]["negative"] += 1
        if arrival is not None and arrival < 0: field_quality["该批次累计到货数量"]["negative"] += 1
        category_rows[category or "<空白>"].append(r)
        if code: product_rows[code].append(r)
        if barcode: barcode_rows[barcode].append(r)
        key = (code, production, expiry)
        if code and expiry: batch_rows[key].append(r)
        reasons = []
        if not code: reasons.append("missing_product_code")
        if not expiry: reasons.append("missing_or_invalid_expiry_date")
        if life is None or life <= 0: reasons.append("missing_invalid_or_nonpositive_shelf_life")
        if unit not in {"D", "M", "Y"}: reasons.append("missing_or_invalid_shelf_life_unit")
        if stock is None or stock < 0: reasons.append("missing_invalid_or_negative_stock")
        if production is None: reasons.append("missing_or_invalid_production_date_for_actual_days")
        if production and expiry and expiry < production: reasons.append("expiry_before_production")
        if reasons:
            invalid_reasons.update(reasons)
        else:
            r.update(category=category or "<空白>", code=code, barcode=barcode, name=name, production=production, expiry=expiry, life=life, unit=unit, stock=stock, arrival=arrival, actual_days=(expiry-production).days)
            usable.append(r)

    conflict_fields = ("商品条码", "商品名称", "保质期", "保质期单位", "是否该做临期折扣", "该批次累计到货数量")
    batch_conflicts = 0
    exact_duplicates = 0
    conflicting_keys = set()
    for key, group in batch_rows.items():
        signatures = {tuple(clean(row[f]) for f in conflict_fields) for row in group}
        if len(group) > 1:
            if len(signatures) == 1: exact_duplicates += len(group) - 1
            else:
                batch_conflicts += 1
                conflicting_keys.add(key)

    category_profile = {}
    for category, items in sorted(category_rows.items()):
        codes = {clean(x["商品编码"]) for x in items if clean(x["商品编码"])}
        keys = {(clean(x["商品编码"]), date_value(x["生产日期"]), date_value(x["有效日期"])) for x in items if clean(x["商品编码"]) and date_value(x["有效日期"])}
        expired = sum(1 for x in items if date_value(x["有效日期"]) and date_value(x["有效日期"]) < BUSINESS_DATE)
        category_profile[category] = {"rows": len(items), "products_by_product_code": len(codes), "batch_keys": len(keys), "expired_rows": expired}

    schemes = {name: collections.Counter() for name in SCHEMES}
    simulation_by_category = {cat: {"products_by_product_code": 0, "eligible_batches": 0, "expired_batches": 0, "executable_batches": 0, "stock_zero_batches": 0, "unable_to_classify_batches": 0, "schemes": {name: {"follow_up": 0, "history_baseline": 0} for name in SCHEMES}} for cat in category_profile}
    unique_usable = {}
    for r in usable:
        key = (r["code"], r["production"], r["expiry"])
        if key not in conflicting_keys and key not in unique_usable:
            unique_usable[key] = r
    for r in unique_usable.values():
        p = simulation_by_category[r["category"]]
        p["eligible_batches"] += 1
        if r["expiry"] < BUSINESS_DATE: p["expired_batches"] += 1
        current = stage(r["expiry"], r["life"], r["unit"])
        if r["stock"] == 0: p["stock_zero_batches"] += 1
        elif current in {"discount_50", "discount_20", "withdraw"}: p["executable_batches"] += 1
        if r["expiry"] < BUSINESS_DATE and r["stock"] != 0:
            for name, (ratio, low, high) in SCHEMES.items():
                days = window(r["actual_days"], ratio, low, high)
                if (BUSINESS_DATE - r["expiry"]).days <= days:
                    schemes[name]["follow_up"] += 1; p["schemes"][name]["follow_up"] += 1
                else:
                    schemes[name]["history_baseline"] += 1; p["schemes"][name]["history_baseline"] += 1
    usable_row_numbers = {r["row"] for r in usable}
    for cat, p in simulation_by_category.items():
        p["products_by_product_code"] = category_profile[cat]["products_by_product_code"]
        invalid_rows = sum(1 for r in rows if (clean(r["商品大类"]) or "<空白>") == cat and r["row"] not in usable_row_numbers)
        conflict_keys_in_category = {key for key in conflicting_keys if (clean(batch_rows[key][0]["商品大类"]) or "<空白>") == cat}
        p["unable_to_classify_batches"] = invalid_rows + len(conflict_keys_in_category)
    life_days = [r["actual_days"] for r in usable]
    life_by_unit = collections.Counter(r["unit"] for r in usable)
    result = {
        "source": {"path": str(source), "bytes": size, "sha256": before, "business_date": BUSINESS_DATE.isoformat()},
        "workbook": {"sheets": sheets, "required_headers_missing": missing},
        "totals": {"source_rows": len(rows), "unique_product_codes": len(product_rows), "candidate_batch_keys": len(batch_rows), "usable_unique_batches": len(unique_usable), "usable_rows": len(usable), "unusable_rows": len(rows)-len(usable), "exact_duplicate_rows": exact_duplicates, "conflicting_batch_keys": batch_conflicts, "nonunique_barcodes": sum(1 for v in barcode_rows.values() if len({clean(x["商品编码"]) for x in v}) > 1)},
        "field_quality": {k: counter_dict(v) for k, v in field_quality.items()},
        "invalid_reasons": counter_dict(invalid_reasons),
        "categories": category_profile,
        "identity_quality": {"product_codes_with_multiple_names": sum(1 for v in product_rows.values() if len({clean(x["商品名称"]) for x in v}) > 1), "product_codes_with_multiple_barcodes": sum(1 for v in product_rows.values() if len({clean(x["商品条码"]) for x in v}) > 1)},
        "shelf_life_actual_days": {"count": len(life_days), "min": min(life_days) if life_days else None, "max": max(life_days) if life_days else None, "median": sorted(life_days)[len(life_days)//2] if life_days else None, "unit_counts": counter_dict(life_by_unit)},
        "simulation": {"eligible_unique_batch_basis": "product_code + production_date + expiry_date; first row only for identical keys", "schemes": {name: dict(value) for name, value in schemes.items()}, "by_category": simulation_by_category, "stage_window_reverse": "existing authoritative calculation: D/M/Y to 1/30/365 days; total >270 then 90/60/14 else 30/14/7; not a historical-expiry follow-up rule"},
    }
    after = hashlib.sha256(source.read_bytes()).hexdigest().upper()
    if after != before or source.stat().st_size != size:
        raise SystemExit("Source changed during read; no output accepted.")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"source_sha256": before, "source_bytes": size, "usable_unique_batches": len(unique_usable), "output": str(output)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
