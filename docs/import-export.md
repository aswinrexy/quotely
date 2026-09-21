# Import / export invoices

Proforma and commercial invoices for businesses that ship goods across a border: exporters,
importers, traders and agricultural producers.

---

## The one architectural decision

**An import/export invoice IS an `Invoice`.** It is not a parallel document type with its own
tables, numbering, payments and sharing.

```
Invoice  (Type = Standard | ImportExport)
  ├── TradeInvoiceDetails   1:1, only on a trade document
  └── InvoiceItem
        └── TradeLineDetails   1:1, only on a trade line
```

That shape is why payments, the public link, the receivables report, tenant isolation, the invoice
number sequence and the entitlement gate all keep working here without a line of new code. The
only things that are new are the detail a domestic invoice has no concept of, and the layout.

**What was deliberately NOT built:** a second invoice entity, a second line table, a second
numbering sequence, a second sharing mechanism, a second payment path.

### Why the line detail is a companion table

An `InvoiceItem` stays the money-bearing line — quantity, rate, line total, computed by the
existing `InvoiceCalculator`. The shipping facts live beside it.

A parallel `TradeInvoiceItem` table would have put the money on a trade invoice somewhere the
invoice calculator, the payment ledger and the public invoice page had never heard of. Nine
nullable columns on `InvoiceItem` would have sat on every domestic line forever, meaning nothing.

---

## The trap in the reference document

The proforma this feature was built from heads its rate column **"Rate/Kg"** and multiplies by the
**package count**:

```
132 BOXES × ₹590      = ₹77,880    ← what the document prints
435.60 KG  × ₹590     = ₹257,004   ← what the column heading implies
```

A calculator that trusted the label would be wrong by more than triple, with nothing on the page
to show it. So `TradeLineDetails.RateBasis` is stored, never inferred:

| Basis | Multiplies by | Default |
| --- | --- | --- |
| `PerQuantityUnit` | quantity | ✅ |
| `PerNetWeight` | net weight | |

`RateLabel` is kept separately, because the exporter's own column heading is theirs to word even
when it does not describe the arithmetic.

---

## Proforma is not payable

A proforma invoice describes a shipment that has not happened and demands nothing. A Pay button on
one invites a customer to pay against a document their bank will not recognise.

`Invoice.AcceptsPayments` therefore consults `TradeDetails.DefaultPayable`, which is true only for
a commercial invoice. The check **only ever withholds** payment — a commercial invoice still has to
satisfy every rule a domestic invoice does.

The two lists differ for related but distinct reasons:

- **`/api/invoices`** excludes `Type == ImportExport` entirely. Trade documents have their own list
  with their own columns, and a proforma does not belong in a list of what customers owe.
- **Receivables** filters on *payability*, not type. A commercial trade invoice is a real demand
  and belongs in the outstanding total; a proforma would overstate it.

---

## Amount in words

`AmountInWords` picks its grouping from the currency:

| | 150,480 |
| --- | --- |
| INR | One Lakh Fifty Thousand Four Hundred Eighty |
| everything else | One Hundred Fifty Thousand Four Hundred Eighty |

Printing the Indian form on a USD invoice, or the international form on a rupee invoice, both read
as a mistake to the person who matters. Above ninety-nine crore the Indian convention stops
agreeing with itself (*arab*, *kharab*, *lakh crore* are all in use), so it falls back to the
international system rather than printing a confident guess on a financial document.

---

## API

| | |
| --- | --- |
| `GET /api/import-export/invoices` | filters: `search`, `tradeType`, `documentType`, `status`, `customerId` |
| `GET /api/import-export/invoices/{id}` | |
| `POST /api/import-export/invoices` | gated on `Entitlement.CreateInvoice` — a trade document counts as an invoice |
| `PUT /api/import-export/invoices/{id}` | goods are only rewritten while the document is a draft |
| `DELETE /api/import-export/invoices/{id}` | drafts only |
| `POST /api/import-export/invoices/{id}/public-link` | the same service the domestic invoice uses |
| `GET\|POST /api/import-export/invoices/{id}/pdf` | |
| `GET\|PUT /api/import-export/profile` | the business's trade settings |
| `GET /api/import-export/profile/defaults` | what a blank document should contain |

A **standard invoice reached through these routes is a 404**, including the caller's own. A trade
form has no idea about a domestic invoice's tax lines and editing one through it would destroy them.

---

## Defaults

```
value on the document  →  trade profile  →  business profile  →  empty
```

An explicit value on the document always wins: the person filling it in knows something about *this
shipment* that the settings do not. Everything is **snapshotted** when the document is created, so
changing a setting later cannot restate a document a customs broker is already holding.

Editing an existing document deliberately does **not** re-apply defaults — it shows what the
document says, not what the settings say today.

---

## What this feature does not claim

Quotely is a **document builder**. It does not validate against any tariff schedule, verify any
registration number, or know which fields a given consignment, country or commodity requires.

Validation refuses only what is arithmetically impossible — a gross weight below its net weight, a
line priced by weight with no weight. A blank country of origin **saves**, because refusing it would
be claiming an authority Quotely does not have. The form prompts; the server allows.

The declaration wording offered in settings is text some exporters use. Whether any of it applies
to a given business, shipment or notification is not something Quotely can know, and nothing is
written to a document unless the business chooses it.

---

## Migrations

`AddImportExportInvoices` — **purely additive**, on both SQL Server and PostgreSQL.

- `Invoices.Type` (`int`, default `0` = Standard) — every existing invoice reads as exactly what it
  always was, without a single row being touched
- `TradeInvoiceDetails`, `TradeLineDetails`, `TradeProfiles`
- Indexes: `(UserId, Type)` on Invoices, unique on each 1:1 foreign key

No existing table is altered or dropped.

---

## Routes

```
/import-export              list, filterable
/import-export/new          create
/import-export/{id}         detail, PDF, share
/import-export/{id}/edit    edit
/settings/trade             IEC, GST, PAN, APEDA and document defaults
```

Dynamic segments follow the existing static-export convention — see `lib/route-param.ts` and
`public/_redirects`.
