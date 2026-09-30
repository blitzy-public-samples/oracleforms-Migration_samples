# Architecture Decisions — Small Cash Invoice migration

This document records the architecture of the Small Cash Invoice proof point, the gaps this run leaves open, and what phase 2 must do. It cites ids rather than restating rationale:

- `D-xx` — a row of `docs/decision-log.md`, the single source of "why";
- `OI-xx` — an open item of `docs/dependency-open-items.md`, with its handling class and closure code (HIS, P2 or Decision);
- `DR-xx` / `PR-xx` — a domain rule or package-resident rule of `docs/legacy-form-spec.md` §5 and §6.

No Oracle instance exists in this run. Every statement below about package runtime behaviour, binding behaviour or SQL results is UNVERIFIED.

## §1 Context

- **Scope.** A technical-validation pilot, not a production cutover. It replaces the Oracle Forms module `INV_SMALL_CASH` (Forms 14.1.2, read from `05_Complex/Inv_Small_Cash.xml`, D-35) with the .NET 10 solution `SmallCashInvoice.sln`. Oracle 19c, the HIS schema and the three packages `BIL_INVOICE_API`, `BIL_IMPORT` and `BIL_INVOICE_ENGINE` stay exactly as they are.
- **Authority.** The packages govern the rules they implement: .NET calls them through the `BIL_INVOICE_API` wrappers and `BIL_IMPORT` (D-08) and re-codes none of them (D-03, D-33). The Form governs where the packages are silent, and those rules live once in Domain (D-04). The APEX Page 48 export is a reference only.
- **Projects and references.**

  | Project | Responsibility | References |
  |---|---|---|
  | `Billing.Invoicing.Domain` | Form rules no package implements (DR-01 … DR-25), draft models, `InvoiceStatePolicy`, `OpenItemGate` | None |
  | `Billing.Invoicing.Data` | ODP.NET package gateways and binders, Dapper queries, the DR-21 update, transaction ownership, Oracle-error translation, the `Data.Ports` interfaces | Domain; NuGet `Oracle.ManagedDataAccess.Core` and `Dapper` only (D-48) |
  | `Billing.Invoicing.Api` | HTTP endpoints, operator context, `InvoiceWorkflowService`, error contract, DI registration of Data | Domain, Data |
  | `Billing.Invoicing.Web` | Static host for the React 18 build; the client calls Api over HTTP (D-19) | None |
  | `Billing.Invoicing.Tests` | DomainParity, DomainUnit, OracleParity, OracleIntegration, DataUnit, Orchestration and Compliance tests (D-29) | Domain, Data, Api (D-55) |

- **Oracle access.** Package records and tables are bound through anonymous PL/SQL blocks with scalar and associative-array binds, so no schema type is needed (D-09). Data owns every transaction, because the ingested packages never commit (D-10). Atomicity across the missing posting packages is UNVERIFIED.
- **User interface.** React 18 now, two screens for the canvases `CANVAS2` and `MORE` (D-18). The phase 2 target is .NET 10 + Angular (D-01).
- **Gap policy.** A blocking gap throws `NotImplementedException` carrying its OI id and returns 501. An advisory gap lets the operation succeed and lists the id in `openItems` (D-16). OI-20 is the one logged exception (D-45).
- **Offline run.** Nothing executed against Oracle. `ORACLE_TEST_CONNECTION` is unset, so every `[OracleFact]` test skips at discovery (D-22), and every Oracle-dependent statement in code, tests and documents is UNVERIFIED.

## §2 Gaps left by this run, and the action closing each

No closing action edits `05_Complex/**`, the root `README.md` or any Oracle object within this migration. A package or schema change happens outside it (§4).

| Gap | Closing action |
|---|---|
| Package-resident rules unverified: PR-01 … PR-25 skip without a connection, and PR-04, PR-09, PR-10, PR-23 and PR-24 are pending evidence only (D-50) | Provision a seeded, disposable, non-production HIS test schema. Inspect `BIL_PAYMENT`, `BIL_QUEUE_POSTING`, `BIL_STOCK_POSTING`, `BIL_AUDIT`, `BIL_MESSAGE` and `BIL_REPORTS_PRINT`, and record the result in the isolation record of `docs/dependency-open-items.md` §4; its status line may read `Cleared` only under the conditions that section sets. Set `ORACLE_TEST_CONNECTION` and run `dotnet test SmallCashInvoice.sln -c Release --filter "Category=OracleParity" -p:CollectCoverage=false`, then the same with `Category=OracleIntegration`. Write expected values for the pending cases once their packages are ingested: PR-04 (OI-06), PR-09 (OI-19), PR-10 (OI-01), PR-23 (OI-08 … OI-10), PR-24 (OI-03) |
| Fourteen shared packages absent (OI-01 … OI-14); deployment state of the three `BIL_*` packages unknown (OI-16) | Ingest the sources of `BIL_CASHIER_SHIFT`, `BIL_TYPES`, `BIL_PATIENT_CONTEXT`, `BIL_SERVICE_CONTEXT`, `BIL_PRICE_RULE`, `BIL_CLASS_RULE`, `BIL_OFFER_RULE`, `BIL_PAYMENT`, `BIL_QUEUE_POSTING`, `BIL_STOCK_POSTING`, `BIL_REPORTS_PRINT`, `BIL_MESSAGE`, `BIL_AUDIT` and `INS_COMP_UTIL`. Confirm that `BIL_INVOICE_API`, `BIL_IMPORT` and `BIL_INVOICE_ENGINE` are deployed and valid in the HIS test schema. Re-check the gateway signatures, the error register and `OracleErrorCatalog` (D-24), and the binder sizes. Then settle the shift switch and admin bypass (D-25), Partial and Overpaid acceptance (D-13) and the posting-stage equivalences (D-14) |
| Schema DDL absent (OI-15, OI-40, OI-41): the 55 objects OI-15.01 … OI-15.28, OI-40.01 … OI-40.07 and OI-41.01 … OI-41.20 | Ingest a DDL export of the 55 tables, views and sequences. Re-check the column names, types and sizes behind every Direct-query member, and the `%type` widths the binders follow (OI-15.01). Confirm the packages compile against every OI-40 object. Confirm the `DEPT_WISE` / `CALL` column defaults (D-15) and the request-id key of `BIL_INVOICE_CREATE_REQUEST` (D-54) |
| APEX runtime fallbacks (OI-17) and the import-preview collection (OI-18) | Confirm that `FND_APP_SECURITY` exists in the HIS schema; the fallback stays unreached while .NET supplies user, centre and machine. Product decision on whether any client needs `BUILD_IMPORT_PREVIEW_COLLECTION` (D-11) |
| Form-called DB functions absent (OI-19 … OI-24, OI-26, OI-42, OI-43, OI-44) | Ingest each function and replace the throwing member or omitted behaviour that stands in for it: `GET_NEXT_INVOICE_NO` (OI-19, D-02), making PR-09 assertable; `VALIDATE_TOTAL_INV` (OI-20), restoring T005's pre-commit veto (D-45); `GET_ELLIGABILTY` (OI-21), ending the null pre-authorisation deviation (D-52); `DAY_TO_DAYES` (OI-22), enabling the DR-04 age check; `GET_PAYID_VALUE` (OI-23), unblocking credit invoices with a deductible or an advanced class (D-51); `GET_PRICE_PLAN` (OI-24), unblocking the `SERVICES` list and a first package-only import (D-38); `SEND_MESSAG` (OI-26), with OI-47 for the barcode SMS; `GET_HTFN2` (OI-42), for the reservation-time display; `FIND_PROMPT` (OI-43), for prompt translation (D-49); `SILENT_COMMET00` (OI-44), with OI-10 for the store transfer (D-46) |
| Printing and documents (OI-11, OI-27, OI-45 … OI-49) | Ingest `BIL_REPORTS_PRINT` with its `t_print_result` fields, report `XX` (`nat.rdf`), the `rwservlet` configuration and the JSP sources `inv_small_cash`, `inv_form2`, `PAT_CARD_INV`, `iqama_check` and `LIST1111`. Implement `BilInvoiceApiGateway.BuildPrintUrl`, `LegacyExternalCalls.BuildLegacyDocument` and `LegacyExternalCalls.SendInvoiceSms` (with OI-12, D-28), then revisit the `p_build_print_url` and `p_send_sms` flags of create (D-13) |
| Package consumption (OI-31), uncarried columns (OI-33, including the `DEPT_WISE` / `CALL` saves refused under D-53), `OKA` sub-rules (OI-32) | Decide ownership with the package owners: a package operation for `PKG_INV` consumption (D-30); package inputs or a package-owned write for the OI-33 columns (D-15); package evidence for each untraced sub-rule, or a recorded decision to build it elsewhere (D-05). Any package change happens outside this migration |
| Saved-invoice editing (OI-56) | Product decision. If the capability stays, it needs a package-owned update operation, because .NET writes no invoice tables (D-36); `InvoiceWorkflowService.Update` then calls it |
| Operator identity and permissions (OI-25, OI-30) | Ingest the calling module or menu that sets the globals, and `GET_U_PREV20`. Replace the `X-His-*` development headers with authentication and real operator context (D-17) |
| Other Forms (OI-28, OI-50, OI-51, OI-52) and object libraries (OI-29, OI-53, OI-54, OI-55) | Migrate `INVOICE_PAYMENT`, `phy_req_note`, `translate` and `st_trans` in phase 2. Ingest `HMISTEXT.olb`, `HMISBUTTON.olb`, `BUSINESSXP.olb` and `HMISCANVAS.olb`, and inventory any logic defined only there |
| APEX-only behaviours (OI-34 … OI-39, OI-57, OI-58) | Product decision per item. An adopted item gets its decision-log row and traceability rows before it is built |
| Equivalence of the XML export to the `.fmb` (D-35) | Regenerate the XML from `05_Complex/Inv_Small_Cash.fmb` with Forms `frmf2xml` into a location outside the repository, diff it against `05_Complex/Inv_Small_Cash.xml`, and re-run `TraceabilityMatrixCoverageTests` on any difference. The committed export is never overwritten |
| Parity evidence observed on the live system | Record a golden master: run the legacy Form and the packages on the seeded schema for each fixture case under `tests/Billing.Invoicing.Tests/Parity/fixtures/`, capture the outputs, and reconcile them with the derived expected values before either is trusted. Record each divergence in the decision log |

## §3 Phase 2 ingestion list

- **Shared packages (OI-01 … OI-14):** `BIL_CASHIER_SHIFT` (with `ASSERT_CAN_CREATE_INVOICE`), `BIL_TYPES`, `BIL_PATIENT_CONTEXT`, `BIL_SERVICE_CONTEXT`, `BIL_PRICE_RULE`, `BIL_CLASS_RULE`, `BIL_OFFER_RULE`, `BIL_PAYMENT`, `BIL_QUEUE_POSTING`, `BIL_STOCK_POSTING`, `BIL_REPORTS_PRINT`, `BIL_MESSAGE`, `BIL_AUDIT`, `INS_COMP_UTIL`.
- **Package deployment (OI-16):** the deployed state of `BIL_INVOICE_API`, `BIL_IMPORT` and `BIL_INVOICE_ENGINE` in a seeded, disposable HIS test schema.
- **Schema objects, direct query (OI-15.01 … OI-15.28):** `T_INV`, `D_INV`, `TRANS_M`, `PATIENT`, `V_PAT_DATA`, `PAT_VISIT_M`, `PAT_SERV_REQ`, `V_SERVICES_REQ`, `SERVICES`, `PACKAGE_DTL`, `SERVICECAT`, `V_VALID_MAIN_CO`, `COMPANYS`, `DISC_CLASSES`, `PAY_TYPES`, `DOCTORS`, `CLINICS`, `DOC_DATES`, `OFFERS`, `INVOICES_TYPE`, `CURRENCIES`, `PREF`, `USERS_TABLE`, `CASH_CARD_DISC`, `PRICE_PLAN_M`, `PRICE_LIST_MASTER`, `EMP`, `BIL_INVOICE_CREATE_REQUEST`.
- **Schema objects, inside a retained package call (OI-40.01 … OI-40.07):** `BIL_REQUEST_INV_SELECTION`, `BIL_INV_DRAFT_LINES`, `D_INV_SEQ`, `PRICE_PLAN_DTL`, `DOCTOR_CONSULTATION`, `OFFERS_DTL`, `FND_USERS`.
- **Schema objects, not used (OI-41.01 … OI-41.20):** `T_INV_LOCA`, `D_INV_LOCA`, `D_INV_LOCA_SEQ`, `PAT_VISIT_M_SEQ`, `DOC_SEQ`, `CASHIER_SHIFT`, `HMISFOX_AUDIT`, `PACKAGE_CONS_M`, `PACKAGE_CONS`, `CASH_CARD_DISCDTL`, `COMPANY_CAT`, `NATIONALITY`, `V_USER_PREV`, `PC_PRINT_DEF`, `TMP_SEQ`, `TRANS_DTL`, `TRANS_DTL_SEQ`, `ITEMS`, `SYSTEMS`, `V_SERV_REQ_TO_INV`.
- **APEX runtime fallback (OI-17):** `FND_APP_SECURITY`.
- **Standalone functions:** `GET_NEXT_INVOICE_NO` (OI-19), `VALIDATE_TOTAL_INV` (OI-20), `GET_ELLIGABILTY` (OI-21), `DAY_TO_DAYES` (OI-22), `GET_PAYID_VALUE` (OI-23), `GET_PRICE_PLAN` (OI-24), `GET_U_PREV20` (OI-25), `SEND_MESSAG` (OI-26), `GET_HTFN2` (OI-42), `FIND_PROMPT` (OI-43), `SILENT_COMMET00` (OI-44).
- **Calling module (OI-30):** the module or menu that opens `INV_SMALL_CASH` and sets its globals and parameters.
- **Reports (OI-11, OI-27):** report `XX` (`nat.rdf`) and the `rwservlet` report-server configuration.
- **JSP sources:** `inv_small_cash.jsp` (OI-45), `inv_form2.jsp` (OI-46), `PAT_CARD_INV.jsp` (OI-47), `iqama_check.jsp` (OI-48), `LIST1111.jsp` (OI-49).
- **Object libraries:** `HMISTEXT.olb` (OI-29), `HMISBUTTON.olb` (OI-53), `BUSINESSXP.olb` (OI-54), `HMISCANVAS.olb` (OI-55).
- **Other Forms:** `INVOICE_PAYMENT` (OI-28), `phy_req_note` (OI-50), `translate` (OI-51), `st_trans` (OI-52).
- **Forms tooling (D-35):** Forms 14.1.2 with `frmf2xml`, to regenerate the export from `05_Complex/Inv_Small_Cash.fmb`.

OI-18, OI-34 … OI-39, OI-56, OI-57 and OI-58 need a product decision rather than an ingestion.

## §4 What carries into phase 2, and what changes

**Reusable boundaries.** These are the seams phase 2 builds on:

- **Domain:** the rule classes DR-01 … DR-25, `InvoiceStatePolicy` and `OpenItemGate`, with their JSON fixtures under `tests/Billing.Invoicing.Tests/Parity/fixtures/`. Domain depends on neither UI nor Oracle.
- **Data:** the gateways `BilInvoiceApiGateway` and `BilImportGateway`, and the binders `HeaderInputBinder`, `LineInputBinder`, `ClientIdBinder` and `OutputArrayReader` over `BIL_INVOICE_ENGINE.T_HEADER_INPUT` / `T_LINE_INPUT_TAB` (D-09, binding UNVERIFIED); the bind-width guard `BoundedVarchar2` that both gateways share (D-84); the `Data.Ports` interfaces (D-55); the transaction owner `OracleSession` (D-10).
- **Error contract:** 422 `oracle-business-error`, `field-validation` and `operator-context-missing`; 501 `open-item`; 503 `oracle-unavailable`; 500 `oracle-error`; 404 `not-found` (D-77); catalogue matching on package, number and message prefix (D-24).
- **Api:** the routes and the request and response contracts in `Billing.Invoicing.Api.Contracts`. The Angular client replaces the React client over the same contracts, component for component: `InvoiceScreen`, `MoreDetailsScreen`, `LovPicker`, `InvoiceHeaderForm`, `InvoiceLinesGrid`, `TotalsPanel`, `PaymentPanel`, `FieldMessage`, `ConnectivityBanner`, `OpenItemNotice` (D-01).
- **Compliance:** `TraceabilityMatrixCoverageTests`, `ErrorRegisterCoverageTests`, `NoDdlInGeneratedSourceTests` and `DomainIsolationTests` keep the matrix, the error register, the no-DDL check and the Domain isolation in force as phase 2 changes land (D-56).

**Changes phase 2 must make.**

- Replace each throwing member as its open item closes: `LegacyExternalCalls` (OI-10, OI-12, OI-20 … OI-24, OI-26, OI-44, OI-45, OI-47, OI-48), `PackageConsumptionGateway.Begin` (OI-31), `BilInvoiceApiGateway.BuildPrintUrl` (OI-11) and `InvoiceWorkflowService.Update` (OI-56). Remove the matching `openItems` entries and 501 branches, and update the register and the traceability matrix.
- Implement `ValidateTotalInvoice` so that its failure makes the create attempt a rollback before the commit (D-45).
- Replace the `X-His-*` development headers with authentication and real operator context (OI-30, D-17).
- Extend the LOV bind policy to multiple languages and the reservation switch (D-49).
- Revisit with the package owners the OI-32 blockers (D-05), the OI-23 deductible block (D-51), the `DEPT_WISE` / `CALL` refusal (OI-33, D-53) and the OI-56 editing decision (D-36).
- Turn the pending-evidence parity cases into asserted ones (D-50).
- Record each phase 2 decision as a new `D-xx` row of `docs/decision-log.md`.

Any change to `BIL_INVOICE_API`, `BIL_IMPORT`, `BIL_INVOICE_ENGINE`, a shared package or any other Oracle object happens outside this migration, under its owners; the solution then calls the changed package as it stands.
