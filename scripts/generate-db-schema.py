#!/usr/bin/env python3
"""
Regenerates the "Schema" section of docs/database.md from the live database (after migrations), so the description of
tables, columns and constraints always matches the migration. Requires the compose stack (postgres service):

    docker compose up -d postgres api      # api applies the migrations
    python scripts/generate-db-schema.py

Table purposes come from TABLES below (hand-written); everything else is read from pg_catalog.
"""
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "docs" / "database.md"
BEGIN, END = "<!-- schema:begin -->", "<!-- schema:end -->"

# table -> (group, purpose). Groups give the order of the document.
TABLES = {
    "products": ("Catalog", "OTC products: name, EAN, category, price (`price_amount` + `price_currency`), active flag. Searched through `pg_trgm`."),
    "pharmacies": ("Pharmacies and stock", "Partner pharmacies: code, address, PostGIS location (`geography(Point)`), opening hours as `jsonb` (time zone America/New_York)."),
    "stock_items": ("Pharmacies and stock", "Stock of a product at a pharmacy: `on_hand` (on the shelf) and `reserved`; `available = on_hand − reserved`. The row is locked `FOR UPDATE` during a reservation; `fillfactor = 80` for HOT updates."),
    "stock_reservations": ("Pharmacies and stock", "Reservations of order lines: `Active` → `Committed` (goods handed over) or `Released` (cancellation, reassignment, expiry after `expires_at`)."),
    "orders": ("Orders", "Order aggregate: customer, fulfilling pharmacy, state machine status, total, delivery address (`jsonb`), ETA, checkout idempotency key, pharmacies that rejected the order. Optimistic concurrency through `xmin`."),
    "order_items": ("Orders", "Order lines with a snapshot of the name and the price at checkout time."),
    "order_status_history": ("Orders", "History of status transitions (who/when/why) — the timeline on the customer's page."),
    "payments": ("Payments", "Payments at the PSP; the unique `(provider, provider_payment_id)` guarantees webhook idempotency at the database level."),
    "couriers": ("Deliveries", "Couriers (linked to a user account) and their availability status."),
    "deliveries": ("Deliveries", "Delivery of an order: courier, statuses `Assigned` → `InDelivery` → `Delivered`. Partial unique indexes: a courier has one active delivery, an order — one non-cancelled delivery."),
    "courier_locations": ("Deliveries", "History of GPS points (append-only), partitioned by day on `recorded_at`; 30-day retention through `DROP PARTITION`. The latest position lives in Redis, not here."),
    "users": ("Identity", "Accounts: e-mail (unique, case-insensitive), PBKDF2 password hash, role, link to a pharmacy/courier, failed login counter and lockout."),
    "password_history": ("Identity", "Hashes of previous passwords — the last N may not be reused."),
    "auth_sessions": ("Identity", "Device session (one refresh token family): user agent, IP, last use, revocation."),
    "refresh_tokens": ("Identity", "Refresh tokens — SHA-256 only (`token_hash`); rotation (`used_at`, `replaced_by_id`) and reuse detection."),
    "access_tokens": ("Identity", "Issued access tokens by `jti` — they allow revoking a token immediately (logout, password change), before it expires."),
    "audit_entries": ("Audit", "Audit entry of an API action marked `[Audited]`: action, user, trace id, time."),
    "audit_changes": ("Audit", "Entity changes within an entry: entity, id, property, `old_value` → `new_value` (secrets masked)."),
    "inbox_state": ("MassTransit (outbox/inbox)", "MassTransit inbox: deduplication of received messages (a consumer processes a message once)."),
    "outbox_message": ("MassTransit (outbox/inbox)", "Outbox: messages saved in the same transaction as the aggregate change and sent to RabbitMQ afterwards."),
    "outbox_state": ("MassTransit (outbox/inbox)", "Outbox delivery state (`FOR UPDATE SKIP LOCKED` lock between instances)."),
    "__ef_migrations_history": ("Technical", "Applied EF Core migrations."),
}

# PostGIS catalog table — not part of the application schema.
IGNORED = {"spatial_ref_sys"}

COLUMNS_SQL = """
select cl.relname, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull, coalesce(pg_get_expr(d.adbin, d.adrelid), ''),
       case when a.attidentity <> '' then 'identity' else '' end
from pg_attribute a
join pg_class cl on cl.oid = a.attrelid
join pg_namespace n on n.oid = cl.relnamespace
left join pg_attrdef d on d.adrelid = a.attrelid and d.adnum = a.attnum
where n.nspname = 'public' and cl.relkind in ('r', 'p') and not cl.relispartition and a.attnum > 0 and not a.attisdropped
order by cl.relname, a.attnum;
"""

CONSTRAINTS_SQL = """
select conrelid::regclass::text, conname, contype, pg_get_constraintdef(oid), coalesce(confrelid::regclass::text, '')
from pg_constraint
where connamespace = 'public'::regnamespace and contype in ('p', 'u', 'f', 'c', 'x')
  and conrelid in (select oid from pg_class where not relispartition)
order by 1, contype, 2;
"""

UNIQUE_INDEX_SQL = """
select t.relname, i.relname, pg_get_indexdef(i.oid)
from pg_index x join pg_class i on i.oid = x.indexrelid join pg_class t on t.oid = x.indrelid
join pg_namespace n on n.oid = t.relnamespace
where n.nspname = 'public' and x.indisunique and not x.indisprimary and not t.relispartition
  and not exists (select 1 from pg_constraint c where c.conindid = i.oid)
order by 1, 2;
"""


def query(sql):
    out = subprocess.run(["docker", "compose", "exec", "-T", "postgres", "psql", "-U", "pharmacy_delivery", "-d", "pharmacy_delivery", "-At", "-F", "\x1f", "-c", sql],
                         cwd=ROOT, check=True, capture_output=True, text=True, encoding="utf-8").stdout
    return [line.split("\x1f") for line in out.splitlines() if line]


def unquote(name):
    return name.strip('"')


def main():
    columns = {}
    for table, column, type_, notnull, default, identity in query(COLUMNS_SQL):
        if table not in IGNORED:
            columns.setdefault(table, []).append((column, type_, notnull == "t", default or identity))

    constraints, fks = {}, []
    for table, name, kind, definition, target in query(CONSTRAINTS_SQL):
        table = unquote(table)
        constraints.setdefault(table, []).append((name, kind, definition))
        if kind == "f":
            fk_cols = re.search(r"FOREIGN KEY \(([^)]*)\)", definition).group(1)
            fks.append((table, unquote(target), fk_cols, name))
    for table, name, definition in query(UNIQUE_INDEX_SQL):
        where = re.search(r"\((.*)\)( WHERE .*)?$", definition)
        constraints.setdefault(table, []).append((name, "u-index", "UNIQUE " + definition.split(" USING ")[1]))

    unknown = sorted(set(columns) - set(TABLES))
    missing = sorted(set(TABLES) - set(columns))
    if unknown or missing:
        raise SystemExit(f"TABLES out of date: undocumented {unknown}, no longer in the database {missing}")

    kinds = {"p": "PK", "u": "UNIQUE", "u-index": "UNIQUE (index)", "f": "FK", "c": "CHECK", "x": "EXCLUDE"}
    out = [BEGIN, "", "## Schema", "",
           "This section is generated from the database after the migrations (`python scripts/generate-db-schema.py`) — do not edit it by hand. "
           "Conventions: `snake_case` names, `uuid` keys (Guid v7 assigned by the application), `timestamptz` times (UTC), amounts as `numeric(12,2)` + currency, "
           "explicit constraint names (mapped to API error codes — section \"Database errors → API\"). Non-unique indexes are described in the \"Indexes\" section.", ""]

    out += ["### ER diagram", "", "```mermaid", "erDiagram"]
    for table, target, cols, name in sorted(set(fks)):
        if table.startswith("__") or target.startswith("__"):
            continue
        out.append(f'    {target} ||--o{{ {table} : "{cols}"')
    for table in TABLES:
        if table.startswith("__") or table not in columns:
            continue
        out.append(f"    {table} {{")
        pk = {c.strip() for n, k, d in constraints.get(table, []) if k == "p" for c in re.search(r"\(([^)]*)\)", d).group(1).split(",")}
        fk_cols = {c.strip() for t, _, cols, _ in fks if t == table for c in cols.split(",")}
        for column, type_, _, _ in columns[table]:
            simple = re.sub(r"[^A-Za-z0-9_]", "_", type_.split("(")[0]).strip("_") or "type"
            key = " PK" if column in pk else " FK" if column in fk_cols else ""
            out.append(f"        {simple} {column}{key}")
        out.append("    }")
    out += ["```", ""]

    group = None
    for table, (table_group, purpose) in TABLES.items():
        if table_group != group:
            group = table_group
            out += [f"### {group}", ""]
        out += [f"#### `{table}`", "", purpose, "", "| Column | Type | NULL | Default |", "|---|---|---|---|"]
        for column, type_, notnull, default in columns[table]:
            out.append(f"| `{column}` | `{type_}` | {'no' if notnull else 'yes'} | {f'`{default}`' if default else ''} |")
        cons = constraints.get(table, [])
        if cons:
            out += ["", "| Constraint | Kind | Definition |", "|---|---|---|"]
            for name, kind, definition in cons:
                out.append(f"| `{name}` | {kinds[kind]} | `{definition.replace('|', '¦')}` |")
        out.append("")
    out.append(END)

    text = DOC.read_text(encoding="utf-8")
    section = "\n".join(out)
    if BEGIN in text:
        text = text[: text.index(BEGIN)] + section + text[text.index(END) + len(END):]
    else:
        anchor = text.index("\n## Indexes")
        text = text[:anchor] + "\n" + section + "\n" + text[anchor:]
    DOC.write_text(text, encoding="utf-8", newline="\n")
    print(f"{DOC}: {len(columns)} tables, {sum(len(c) for c in constraints.values())} constraints, {len(fks)} foreign keys")


if __name__ == "__main__":
    main()
