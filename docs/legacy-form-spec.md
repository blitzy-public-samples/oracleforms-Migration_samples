# Legacy Form Specification — INV_SMALL_CASH

This document specifies the Oracle Forms module `INV_SMALL_CASH` as the migration to `SmallCashInvoice.sln` understood it, and maps every construct to its target. §8 and §9 are machine-read by the Compliance tests `ErrorRegisterCoverageTests` and `TraceabilityMatrixCoverageTests`; their layouts are fixed. Decision ids (`D-xx`) refer to `docs/decision-log.md`; open-item ids (`OI-xx`, `OI-15.xx`, `OI-40.xx`, `OI-41.xx`) refer to `docs/dependency-open-items.md`.

## §1 Overview

- **Module:** `INV_SMALL_CASH`, "05_Complex – Small Cash Invoice" (Billing / Cashier), Oracle Forms 14.1.2, module version 141020000 [05_Complex/Inv_Small_Cash.xml:2-3].
- **Source read:** the XML export `05_Complex/Inv_Small_Cash.xml`, the only Form source. The binary `05_Complex/Inv_Small_Cash.fmb` is not read; the equivalence of the export to it is UNVERIFIED (D-35).
- **Construct counts**, parsed from the export; blocks, items, triggers, program units, LOVs, relations and report objects agree with [05_Complex/README.md:45-54]:

  | Construct | Count | Construct | Count |
  |---|---:|---|---:|
  | Blocks | 5 | Module parameters | 32 |
  | Items | 202 | Alerts | 5 |
  | Triggers | 95 | Relations | 2 |
  | Program units | 30 | Canvases | 2 |
  | LOVs | 15 | Windows | 1 |
  | Record groups | 16 | Report objects | 1 |
  | Visual attributes | 7 | Attached `.pll` libraries | 0 |

- **Object libraries:** four are named as parents through `ParentFilename`, each an open item: `HMISTEXT.olb` (183 items, OI-29), `HMISBUTTON.olb` (19 buttons, OI-53), `BUSINESSXP.olb` (alerts `ERR_ALERT` and `ABOUT`, 7 visual attributes, OI-54), `HMISCANVAS.olb` (the two canvases, OI-55).
- **Authority hierarchy:** the packages `BIL_INVOICE_API`, `BIL_IMPORT` and `BIL_INVOICE_ENGINE` govern the rules they implement; the Form governs where they are silent; the APEX Page 48 export and its JavaScript are references only (§7).
- **Classes.** Every construct carries one or more codes:
  - `DR-xx` — domain rule: the Form implements it and no package does; re-implemented once in `Billing.Invoicing.Domain` (§5).
  - `PR-xx` — package-resident rule: reached only through a retained PL/SQL call from `Billing.Invoicing.Data`; never re-coded (§6).
  - `OI-xx` — open item: needs an object the ingestion lacks; blocked, advisory, inside a retained call, or not built.
  - `N` — not migrated, with the reason (UI mechanics, dead code, empty trigger, unused).
- **UNVERIFIED convention:** no Oracle instance exists in this run. Every statement here about package runtime behaviour, binding behaviour or SQL results is UNVERIFIED.

## §2 Construct inventory

Locators give the start-tag line in `05_Complex/Inv_Small_Cash.xml`.

**Blocks**

| Block [locator] | Definition | Target |
|---|---|---|
| `T_INV` [05_Complex/Inv_Small_Cash.xml:10] | DB block on `t_inv`, 104 items; WHERE `(INVTYPEID <>8 and INVTYPEID <> 9) and PHARMACY_INV_NO is null`, ORDER BY `INV_NO` | `InvoiceHeaderDraft`; `InvoiceQueries.GetInvoice` applies the same WHERE |
| `D_INV` [05_Complex/Inv_Small_Cash.xml:375] | DB block on `d_inv`, 62 items, 5 rows displayed | `InvoiceLineDraft`; Web `InvoiceLinesGrid` |
| `T_INV_TRANS_M` [05_Complex/Inv_Small_Cash.xml:572] | DB block on `trans_m`, 5 items | Read-only list in `InvoiceQueries.GetMoreDetails` |
| `TOOL` [05_Complex/Inv_Small_Cash.xml:715] | Control block, 31 items (buttons, flags) | Screen actions of the two Web screens |
| `TITLE` [05_Complex/Inv_Small_Cash.xml:792] | Control block, 0 items | N — no content |

**Canvases, window, relations, report, alerts, visual attributes**

| Construct [locator] | Definition | Target |
|---|---|---|
| Canvas `CANVAS2` [05_Complex/Inv_Small_Cash.xml:793] | Content canvas of `WINDOW1`; blocks `T_INV` (79 items), `D_INV` (24), `TOOL` (24) | Web `InvoiceScreen` |
| Canvas `MORE` [05_Complex/Inv_Small_Cash.xml:810] | Detail canvas of `WINDOW1`; `T_INV` insurance fields, `D_INV` dental / lens / approval fields, `T_INV_TRANS_M`, two `TOOL` buttons | Web `MoreDetailsScreen` |
| Window `WINDOW1` [05_Complex/Inv_Small_Cash.xml:1110] | The single window hosting both canvases | N — the Web `App` shell switches screens (T050, T086) |
| Relation `T_INV_D_INV` [05_Complex/Inv_Small_Cash.xml:360] | `D_INV.INV_NO = T_INV.INV_NO` | Join in `InvoiceQueries.GetInvoice` |
| Relation `T_INV_T_INV_TRANS_M` [05_Complex/Inv_Small_Cash.xml:361] | `T_INV_TRANS_M.IMP_FROM_T_INV_NO = T_INV.INV_NO` | Join in `InvoiceQueries.GetMoreDetails` |
| Report `XX` [05_Complex/Inv_Small_Cash.xml:1093] | `nat.rdf`, cache / htmlcss; no trigger runs it | OI-27 |
| Alert `ERR_ALERT` [05_Complex/Inv_Small_Cash.xml:5] | Message alert used by `MESSAG` (from `BUSINESSXP.olb`) | Error contract field and form messages |
| Alert `YES_NO` [05_Complex/Inv_Small_Cash.xml:6] | "Save & Print" / "Save" / "Cancel" | Save and Save & Print actions (T079) |
| Alert `DO_DEL` [05_Complex/Inv_Small_Cash.xml:7] | "Delete Invoice?" | N — delete disabled (DR-17) |
| Alert `DISC_ALERT` [05_Complex/Inv_Small_Cash.xml:8] | "Maximum Discount" / "Cancel" | DR-06 choice (`DiscountLimitChoice`) |
| Alert `ABOUT` [05_Complex/Inv_Small_Cash.xml:9] | About box (from `BUSINESSXP.olb`) | N — UI |
| Visual attributes `DISPLAY`, `REQUIRED`, `NORMAL`, `PROMPT_REQUIRED`, `PROMPT_NORMAL`, `PROMPT_DISPLAY`, `CURRENT_RECORD` [05_Complex/Inv_Small_Cash.xml:1103-1109] | Styling from `BUSINESSXP.olb` | N — OI-54 |

**LOVs (11 used by the blocks, 4 unused) and their record groups (16)**

| LOV [locator] · record group [locator] | Binds | Target |
|---|---|---|
| `COMPANY1_2` [05_Complex/Inv_Small_Cash.xml:871] · `COMPANY` [05_Complex/Inv_Small_Cash.xml:1021] | `:global.current_info_center_id` | `LovQueries.Company` |
| `SUB_COMPANY` [05_Complex/Inv_Small_Cash.xml:880] · `SUB_COMP` [05_Complex/Inv_Small_Cash.xml:1026] | `:t_inv.comp_Code` (at most 10 bytes, D-107); the information centre (D-106) | `LovQueries.SubCompany`; rows only for a company of the operator's centre's `COMPANY1_2` list (D-106) |
| `THE_CLASS` [05_Complex/Inv_Small_Cash.xml:876] · `THE_CLASS` [05_Complex/Inv_Small_Cash.xml:1017] | `:SUB_COMP_CODE` (at most 10 bytes, D-107); the information centre (D-106) | `LovQueries.TheClass`; rows only for a sub-company of a company of the operator's centre's `COMPANY1_2` list (D-106) |
| `PAY_TYPE1` [05_Complex/Inv_Small_Cash.xml:921], `PAY_TYPE2` [05_Complex/Inv_Small_Cash.xml:925] · `PAY_TYPE` [05_Complex/Inv_Small_Cash.xml:994] | `:global.lang` (bound 'E') | `LovQueries.PayTypes`, returning only `PAY_TYPE_ID` and `PAY_TYPE_NAME` (D-105); fills `SubPayType` / `SubPayType2`, never `PayType` |
| `DOC` [05_Complex/Inv_Small_Cash.xml:888] · `DOC` [05_Complex/Inv_Small_Cash.xml:1063] | `:global.current_info_center_id` | `LovQueries.Doc` |
| `RESERV_NO` [05_Complex/Inv_Small_Cash.xml:859] · `RESERV_NO` [05_Complex/Inv_Small_Cash.xml:1069] | `:invdate`, `:docidx` (a positive whole number without sign, padding or leading zero, D-107), `:PATIENTNO`, `:global.reserv_system_500` (bound 0); the information centre (D-106) | `LovQueries.ReservNo`, view-only (OI-33), returning only `RESERV_NO`, `THE_TIME` and `PATAINTNO` (D-105), and rows only for an active doctor of the operator's centre's `DOC` list (D-106) |
| `OFFERS` [05_Complex/Inv_Small_Cash.xml:917] · `OFFERS` [05_Complex/Inv_Small_Cash.xml:1089] | `:PAYTYPE` (1 or 2, D-107), `:INVDATE`, `:global.current_info_center_id` | `LovQueries.Offers` |
| `CAT` [05_Complex/Inv_Small_Cash.xml:866] · `CAT` [05_Complex/Inv_Small_Cash.xml:1012] | none | `LovQueries.Cat` |
| `SERVICES` [05_Complex/Inv_Small_Cash.xml:906] · `SERVICES` [05_Complex/Inv_Small_Cash.xml:1034] | `:d_inv.catid`, `:t_inv.list_id`, `:PLAN_CODE`, `:parameter.pkg_inv`, `:PARAMETER.claim_flag` | 501 OI-24 (not executed) |
| `DOC1` [05_Complex/Inv_Small_Cash.xml:884] · `DOC1` [05_Complex/Inv_Small_Cash.xml:1030] | `:global.current_info_center_id` | 501 OI-33 (not executed) |
| `APPROVED_SERV` [05_Complex/Inv_Small_Cash.xml:846] · `APPROVED_SERV` [05_Complex/Inv_Small_Cash.xml:1076] | — | N — attached to no item |
| `CLINICS` [05_Complex/Inv_Small_Cash.xml:894] · `CLINICS` [05_Complex/Inv_Small_Cash.xml:1059] | — | N — attached to no item |
| `PATIENT` [05_Complex/Inv_Small_Cash.xml:898] · `PATIENT` [05_Complex/Inv_Small_Cash.xml:1055] | — | N — the `PATIENTNO` item has an empty `LovName` |
| `PATIENT_TRANS` [05_Complex/Inv_Small_Cash.xml:902] · `PATIENT_TRANS` [05_Complex/Inv_Small_Cash.xml:1008] | — | N — opened only by dead `IMP_RXXX` |
| — · `RECORD_GROUP1196` [05_Complex/Inv_Small_Cash.xml:1001], `SERVICES_BAK` [05_Complex/Inv_Small_Cash.xml:1045] | — | N — referenced by no LOV |

The bind policy of the served LOVs is D-49: `:global.lang` → `'E'`, `:global.reserv_system_500` → `0`, `:global.current_info_center_id` → the `X-His-Info-Center-Id` header, `:invdate` → `DraftDate`, item binds from the current draft (a missing value → 422 `field-validation`).

**Module parameters** [05_Complex/Inv_Small_Cash.xml:814-845]

| Parameter | Line | Read by | Codes | Target |
|---|---|---|---|---|
| `IS_HOME_CARE` | 814 | T015, T022 | DR-20 | `InvoiceEntryParameters.IsHomeCare` → `InvoiceDefaultsRule.Apply` |
| `NEW_PAT_INV` | 815 | No reader outside comments | N | Unused |
| `THE_DOC` | 816 | T029 | DR-11 | `InvoiceEntryParameters.TheDoc` → `DoctorSelectionRules.Validate` |
| `COMP_TYPE` | 817 | Set by T026 from `COMPANYS`; read by T015, T023, T029, T054, T066, `MAKE_CASH`, `OKA`, `CHK_ADV_CLASS` | PR-04 | `InvoiceEntryParameters.CompType`; line payer rate is payer share inside the package (UNVERIFIED) |
| `PAY_VAT_CO` | 818 | T010, T023, `SMALL_CALC` | PR-05 | `InvoiceEntryParameters.PayVatCo`; VAT flags inside the package (UNVERIFIED) |
| `FROM_CHK` | 819 | T003, `CHK_SEC_DETAIL` | N | UI mechanics: item toggling |
| `CLAIM_FLAG` | 820 | T003, T015, T023, T029, T030; `SERVICES` record group | DR-10, DR-20, DR-25, OI-24 | `InvoiceEntryParameters.ClaimFlag` → `ClaimNumberRule.Build`, `InvoiceDefaultsRule.Apply`; also filters the blocked `SERVICES` LOV |
| `CASH_OR_CREDIT` | 821 | T003, T015, T023 | DR-24 | `InvoiceEntryParameters.CashOrCredit` → `PayTypeSelectionRule.Decide` |
| `DO_REVIEW` | 822 | T029 | DR-25 | `InvoiceEntryParameters.DoReview` → `VisitLineRule.Choose` |
| `THE_COUNTRY` | 823 | No reader outside comments | N | Unused |
| `CLAIM_DATE` | 824 | No reader outside comments | N | Unused |
| `PAY_VAT` | 825 | T010, T023, `SMALL_CALC` | PR-05 | `InvoiceEntryParameters.PayVat`; VAT flags inside the package (UNVERIFIED) |
| `ONE_VISIT_960` | 826 | T003, T066, `OKA` | OI-32 | `InvoiceEntryParameters.OneVisit960`; claim / revisit sub-rule blocked |
| `DIRECT_CALL` | 827 | T003, `CHK_SEC_DETAIL` | N | UI mechanics: item toggling |
| `X422_APPROV_CHECK` | 828 | T003, T074, T085 | DR-14, DR-18, PR-19 | `InvoiceEntryParameters.X422ApprovCheck`; the workflow uses the PREF 422 value instead, 1 when it holds no integer (D-73) → `LineEntryRules.ValidateApproval`, `RequestImportRules.Notices`, `BilImportGateway.ApprovalCheckMode` (D-12) |
| `DEDUCT_RATE` | 829 | No reader outside comments | N | Unused |
| `DEDUCT_FIXED` | 830 | No reader outside comments | N | Unused |
| `DIRECT_COMP_SHARE` | 831 | Set by T026 from `COMPANYS`; read by T015, T023, T029, T054, T066, `MAKE_CASH`, `OKA`, `CHK_ADV_CLASS` | PR-04 | `InvoiceEntryParameters.DirectCompShare`; payer share inside the package (UNVERIFIED) |
| `LOCAL_DOC_TYPE` | 832 | T012, T088, `DO_INTERFACE`, `CHG_PRMPT2` | OI-15.01, OI-30, N | `InvoiceEntryParameters.LocalDocType`, server-owned and fixed at 505 → `ROW_TYPE` filter of `InvoiceQueries.GetInvoice` and `InvoiceQueries.GetMoreDetails` (T012, D-111); UI uses not migrated |
| `VISIT_UNIQUE` | 833 | T003, T015, T023, T085 | DR-20, PR-19 | `InvoiceEntryParameters.VisitUnique` → `InvoiceDefaultsRule.Apply`, `InvoiceQueries.GetSelectedRequestRows` |
| `NEW_DOC` | 834 | T015 | DR-20 | `InvoiceEntryParameters.NewDoc` → doctor for claim parameter '1' |
| `WILL_DO_IMP` | 835 | T003, `CHK_SEC_DETAIL` | N | UI mechanics: item toggling |
| `INV_ADMIN` | 836 | T003, T011, T027 | OI-01 | `InvoiceEntryParameters.InvAdmin`; shift bypass not reproduced (D-25); T027 item toggling is UI mechanics |
| `INV_DATE_ADMIN` | 837 | T023 | DR-03 | `InvoiceEntryParameters.InvDateAdmin` → `PatientEligibilityRules.Evaluate`; the workflow sets it to 2 (normal user) for every request (D-98) |
| `OPEN_FROM_ACC` | 838 | No reader outside comments | N | Unused |
| `PKG_INV` | 839 | T015, T066, `OKA` | OI-31 | `InvoiceEntryParameters.PkgInv`; package consumption blocked: `PackageConsumptionGateway.Begin` → 501 |
| `CLAIM_NO` | 840 | T003, T015, T023, T029, T030 | DR-10, DR-20, DR-25 | `InvoiceEntryParameters.ClaimNo` → `ClaimNumberRule.Build`, `InvoiceDefaultsRule.Apply`, `VisitLineRule.Choose` |
| `PHARAMACY_INSTALL_601` | 841 | No reader outside comments | N | Unused |
| `RESERV_BY_TIME_801` | 842 | Filled from `PREF` by T003 | N | Reservation-by-time not migrated: `RESERV_NO` is view-only |
| `SHIFT_CONYTOL_901` | 843 | Filled from `PREF` by T003 | OI-01 | `InvoiceEntryParameters.ShiftConytol901`; shift switch not reproduced (D-25) |
| `LESS_PAYMENT_970` | 844 | Filled by T003, never read | N | Unused (legacy anomaly, §10) |
| `P_USER` | 845 | No reader outside comments | N | Unused |

**Items.** The 202 items are keyed individually in §9.1. Mapping rule: a carried or rule-read item maps to the property that carries it (`InvoiceHeaderDraft.<Name>`, `InvoiceLineDraft.<Name>` or `MoreDetailsResponse.<Name>`); a button maps to its screen action or its open item; a display-only lookup name maps to the response field that returns it, with the schema object queried; an item the package inputs do not carry maps to OI-33; a pure UI item (prompt, toggle, counter, flag) maps to N, "UI mechanics".

## §3 Trigger and program-unit classification

Trigger ids follow XML order: the form-level triggers first, then per block the block-level triggers followed by the item-level triggers. `Owner · event` names the block (or `FORM`), the item where the trigger is item-level, and the event. Swallowed raises (`when others then null`) are warnings (D-27).

**Triggers (95)**

| Id | Locator | Owner · event | Business content | Class | Target member or reason |
|---|---|---|---|---|---|
| T001 | [05_Complex/Inv_Small_Cash.xml:1094] | FORM · WHEN-WINDOW-CLOSED | `EXIT_FORM` | N | UI mechanics: window close |
| T002 | [05_Complex/Inv_Small_Cash.xml:1095] | FORM · POST-FORM | `null` | N | Empty trigger |
| T003 | [05_Complex/Inv_Small_Cash.xml:1096] | FORM · WHEN-NEW-FORM-INSTANCE | System-lock exit ('ORA-00020'); `CHK_SEC` permission; open-shift gate (`SHIFT_CONYTOL_901`, `EXIT_FORM` unless `INV_ADMIN=1`); PREF 960/970/422 reads; `GET_PRICE_PLAN`; claim/visit preload | PR-10, OI-01, OI-30, OI-25, DR-14, DR-20, OI-24, OI-15.22 | Shift gate → PR-10 inside `BilInvoiceApiGateway.CreateFullInvoice`; switch and admin bypass not reproduced (OI-01, D-25); system lock not built (OI-30); permission not built (OI-25); PREF 422 → `LookupQueries.GetPreferences` feeding DR-14; `GET_PRICE_PLAN` blocked (OI-24); preload → DR-20 `InvoiceDefaultsRule.Apply` |
| T004 | [05_Complex/Inv_Small_Cash.xml:1097] | FORM · PRE-FORM | Invoice-type and currency list items from `INVOICES_TYPE`, `CURRENCIES` | OI-15.20, OI-15.21 | `LookupQueries.GetInvoiceTypes`, `LookupQueries.GetCurrencies` through `LookupsController.InvoiceTypes` / `.Currencies` |
| T005 | [05_Complex/Inv_Small_Cash.xml:1098] | FORM · POST-FORMS-COMMIT | `VALIDATE_TOTAL_INV(:t_inv.inv_no)` after the save's DML and before the database commit; a raise failed the save | OI-20 | Blocked check `LegacyExternalCalls.ValidateTotalInvoice`, called at the same point before the commit; its `NotImplementedException` is caught and the create commits, listing OI-20 (D-45) |
| T006 | [05_Complex/Inv_Small_Cash.xml:1099] | FORM · WHEN-FORM-NAVIGATE | Global UI flags (`inv_to_delete`, `DO_PREVIEW`) | N | UI mechanics: form navigation state |
| T007 | [05_Complex/Inv_Small_Cash.xml:1100] | FORM · ON-CLEAR-DETAILS | Master-detail coordination | N | UI mechanics: the API returns header and lines together |
| T008 | [05_Complex/Inv_Small_Cash.xml:1101] | FORM · WHEN-WINDOW-ACTIVATED | Global UI flags (`inv_to_delete`, `DO_PREVIEW`) | N | UI mechanics: window activation state |
| T009 | [05_Complex/Inv_Small_Cash.xml:1102] | FORM · PRE-COMMIT | 'Invoice without Details' when there are no lines | DR-02, PR-14 | `InvoiceDetailRules.RequireDetails`; engine -20903 re-checks (UNVERIFIED) |
| T010 | [05_Complex/Inv_Small_Cash.xml:362] | T_INV · POST-QUERY | Display lookups; makes queried invoices non-updatable and non-deletable | DR-17, OI-15.01 | `InvoiceQueries.GetInvoice` (display lookups in `InvoiceViewResponse.Display`); read-only → `InvoiceStatePolicy.CanEdit` |
| T011 | [05_Complex/Inv_Small_Cash.xml:363] | T_INV · PRE-INSERT | `GET_NEXT_INVOICE_NO` ×20; `OLD_OR_NEW`; shift re-check; `SEQ_NO` / `DOC_SEQ` queue number; PATIENT `NEW_INV_*` clear; `IS_STATE`; `THE_MONTH` / `THE_YEAR`; `machine_n` | PR-09, PR-10, PR-11, PR-12, DR-21, OI-09, OI-19, OI-33 | Numbering PR-09; `OLD_OR_NEW` PR-11; month/year PR-12; shift PR-10 (the re-check only warned); queue number → queue posting (D-14, OI-09, UNVERIFIED); `IS_STATE` not carried (OI-33); PATIENT clear DR-21 `ReceptionTransferRule.ShouldClear` + `PatientTransferCommand.ClearReceptionTransfer` (failure swallowed, D-44) |
| T012 | [05_Complex/Inv_Small_Cash.xml:364] | T_INV · PRE-QUERY | `global.go_to_inv` preset; `LOCAL_DOC_TYPE` → `ROW_TYPE` | OI-15.01 | `ROW_TYPE` filter in `InvoiceQueries.GetInvoice` and `InvoiceQueries.GetMoreDetails`, from the fixed `LOCAL_DOC_TYPE` 505 (D-111) |
| T013 | [05_Complex/Inv_Small_Cash.xml:365] | T_INV · PRE-UPDATE | `UPD_USER_NO`, `PAT_VISIT_M` doctor update | OI-56 | Saved-invoice update blocked: `InvoicesController.Update` → 501 (D-36) |
| T014 | [05_Complex/Inv_Small_Cash.xml:366] | T_INV · WHEN-VALIDATE-RECORD | Patient required; company required for credit; doctor required; payment type default | DR-01, DR-22, PR-13 | `HeaderRecordRules.ValidateRecord`, `HeaderRecordRules.ApplyPaymentTypeDefault`; engine header checks re-check (UNVERIFIED) |
| T015 | [05_Complex/Inv_Small_Cash.xml:367] | T_INV · WHEN-CREATE-RECORD | Defaults: info centre, `INVDATE := sysdate`, `INV_TIME`, `PFLAG`, claim preload, `payed_before` via `GET_PAYID_VALUE`, home-care type 7, `SUB_PAYTYPE := 1` | DR-20, DR-24, PR-12, OI-23 | `InvoiceDefaultsRule.Apply` (claim preload `InvoiceQueries.GetClaimPreload`, visit doctor `LookupQueries.GetVisitDoctor`, draft date `LookupQueries.GetDatabaseTime`, D-39); pay type `PayTypeSelectionRule.Decide`; `PFLAG` PR-12; `payed_before` not built (OI-23) |
| T016 | [05_Complex/Inv_Small_Cash.xml:368] | T_INV · POST-INSERT | `HMISFOX_AUDIT` insert; `ADD_TO_LIST` → `PAT_VISIT_M` ('Cant add visit to clinic list'); cash-invoice SMS link | OI-13, OI-09, OI-12, OI-45 | Audit → engine `BIL_AUDIT` call (OI-13, UNVERIFIED); visit list → queue posting (OI-09, D-14, UNVERIFIED); SMS with the `inv_small_cash.jsp` link not sent (OI-12, OI-45, D-28) |
| T017 | [05_Complex/Inv_Small_Cash.xml:369] | T_INV · POST-UPDATE | Update audit | OI-56 | Saved-invoice update blocked: `InvoicesController.Update` → 501 (D-36) |
| T018 | [05_Complex/Inv_Small_Cash.xml:370] | T_INV · ON-POPULATE-DETAILS | Master-detail coordination | N | UI mechanics: the API returns header and lines together |
| T019 | [05_Complex/Inv_Small_Cash.xml:371] | T_INV · PRE-DELETE | Detail delete | DR-17 | Header delete refused (`InvoiceStatePolicy.CanDelete`), so the detail delete never runs |
| T020 | [05_Complex/Inv_Small_Cash.xml:372] | T_INV · ON-CHECK-DELETE-MASTER | Delete refused | DR-17 | `InvoiceStatePolicy.CanDelete`; no delete endpoint |
| T021 | [05_Complex/Inv_Small_Cash.xml:373] | T_INV · ON-DELETE | Delete refused: 'You Cant Delete Invoice From Here' | DR-17 | `InvoiceStatePolicy.CanDelete`; no delete endpoint |
| T022 | [05_Complex/Inv_Small_Cash.xml:13] | T_INV.INVTYPEID · WHEN-VALIDATE-ITEM | `IS_HOME_CARE` → type 7; `IS_STATE` 1 for types 1, 3 | DR-20, OI-33 | Home-care type in `InvoiceDefaultsRule.Apply`; `IS_STATE` not carried (OI-33) |
| T023 | [05_Complex/Inv_Small_Cash.xml:18] | T_INV.PATIENTNO · WHEN-VALIDATE-ITEM | Coverage load from `V_PAT_DATA`; cash forcing; contract, company, card, policy, class and referral checks; pay type; `GET_ELLIGABILTY` pre-authorisation | DR-03, DR-24, PR-24, OI-03, OI-24, OI-21 | `PatientEligibilityRules.Evaluate` over `LookupQueries.GetPatientCoverage`; pay type `PayTypeSelectionRule.Decide`; payer context PR-24 via `BIL_PATIENT_CONTEXT` (OI-03); `GET_PRICE_PLAN` not built (OI-24); pre-authorisation not derived (OI-21, D-52) |
| T024 | [05_Complex/Inv_Small_Cash.xml:21] | T_INV.PATIENTNAME · KEY-NEXT-ITEM | Focus navigation | N | UI mechanics: focus navigation |
| T025 | [05_Complex/Inv_Small_Cash.xml:24] | T_INV.INV_TIME · WHEN-VALIDATE-ITEM | `PFLAG` from time | PR-12 | Engine derives `PFLAG` (D-06, UNVERIFIED) |
| T026 | [05_Complex/Inv_Small_Cash.xml:27] | T_INV.COMP_CODE · WHEN-VALIDATE-ITEM | `DIRECT_COMP_SHARE`, `COMP_TYPE` from `COMPANYS`; company '0' → cash, otherwise credit (its `V_type` branches are dead) | DR-24, PR-04 | `PayTypeSelectionRule.Decide` with `LookupQueries.GetCompanyType`; direct share PR-04 |
| T027 | [05_Complex/Inv_Small_Cash.xml:36] | T_INV.PAYTYPE · WHEN-VALIDATE-ITEM | Cash conversion (`MAKE_CASH`), item enablement | PR-24, OI-03 | Payer context PR-24 (OI-03); item enablement is UI mechanics |
| T028 | [05_Complex/Inv_Small_Cash.xml:37] | T_INV.PAYTYPE · WHEN-LIST-CHANGED | Item enablement | N | UI mechanics: item enablement |
| T029 | [05_Complex/Inv_Small_Cash.xml:43] | T_INV.DOCIDX · WHEN-VALIDATE-ITEM | Doctor required / lock warnings; clinic from doctor; `CLAIM_NO` build; waiting counters; consultation / review auto line; cash-card discount; `ADD_TO_LIST` | DR-10, DR-11, DR-23, DR-25, PR-21, OI-32 | `DoctorSelectionRules.Validate`, `ClaimNumberRule.Build`, `AddToListRule.Derive`, `VisitLineRule.Choose` (service '2000' branch) + PR-21 `BilImportGateway.GetVisitLine`; clinic from the `DOC` LOV return; cash card blocked (OI-32); waiting counters display-only (D-18) |
| T030 | [05_Complex/Inv_Small_Cash.xml:44] | T_INV.DOCIDX · KEY-NEXT-ITEM | With `PARAMETER.CLAIM_NO=1`, inserts the doctor's consultation line | PR-21 | `BilImportGateway.GetVisitLine` through `ImportsController.VisitLine` |
| T031 | [05_Complex/Inv_Small_Cash.xml:47] | T_INV.CLINICID · WHEN-VALIDATE-ITEM | Sex and age suitability (warnings: `when others then null` swallows the raise); `payed_before` | DR-04, OI-22, OI-23 | `ClinicSuitabilityRules.CheckSex`; `CheckAge` needs `DAY_TO_DAYES` (OI-22); `payed_before` not built (OI-23) |
| T032 | [05_Complex/Inv_Small_Cash.xml:50] | T_INV.DEPT_WISE · WHEN-VALIDATE-ITEM | GP-at-ER-only rule (blocking), tests `NVL(:DEPT_WISE,0)=1` | DR-05 | `ErClinicRule.Validate` |
| T033 | [05_Complex/Inv_Small_Cash.xml:53] | T_INV.CALL · WHEN-VALIDATE-ITEM | GP-at-ER-only rule (blocking), tests `NVL(:DEPT_WISE,0)=1` when `CALL` changes | DR-05 | `ErClinicRule.Validate` |
| T034 | [05_Complex/Inv_Small_Cash.xml:57] | T_INV.DOCID1 · KEY-NEXT-ITEM | Focus navigation | N | UI mechanics: focus navigation |
| T035 | [05_Complex/Inv_Small_Cash.xml:68] | T_INV.DISC_T · WHEN-VALIDATE-ITEM | Switches percent vs amount entry | N | UI mechanics; the mode is `InvoiceHeaderDraft.DiscT` |
| T036 | [05_Complex/Inv_Small_Cash.xml:69] | T_INV.DISC_T · WHEN-LIST-CHANGED | Switches percent vs amount entry | N | UI mechanics; the mode is `InvoiceHeaderDraft.DiscT` |
| T037 | [05_Complex/Inv_Small_Cash.xml:70] | T_INV.DISC_T · WHEN-MOUSE-CLICK | Switches percent vs amount entry | N | UI mechanics; the mode is `InvoiceHeaderDraft.DiscT` |
| T038 | [05_Complex/Inv_Small_Cash.xml:73] | T_INV.FINALDISC_PERC · KEY-NEXT-ITEM | `SUB_PAYTYPE := 1` when patient pays; navigation | DR-22 | `HeaderRecordRules.ApplyPaymentTypeDefault`; navigation is UI mechanics |
| T039 | [05_Complex/Inv_Small_Cash.xml:74] | T_INV.FINALDISC_PERC · WHEN-VALIDATE-ITEM | `USERS_TABLE.MAX_DISC` limit + `DISC_ALERT`; final discount from percent; amount reset | DR-06, DR-07, PR-06 | Limit `FinalDiscountLimitRule.Evaluate` / `.ApplyChoice`; reset `PaymentAllocationRules.ResetAfterDiscountChange`; amount PR-06 |
| T040 | [05_Complex/Inv_Small_Cash.xml:77] | T_INV.FINALDISC · KEY-NEXT-ITEM | `SUB_PAYTYPE := 1` when patient pays; navigation | DR-22 | `HeaderRecordRules.ApplyPaymentTypeDefault`; navigation is UI mechanics |
| T041 | [05_Complex/Inv_Small_Cash.xml:78] | T_INV.FINALDISC · WHEN-VALIDATE-ITEM | Final discount > patient share blocked; derived percent vs `MAX_DISC` | DR-06, DR-07, PR-06 | Limit `FinalDiscountLimitRule.Evaluate`; exceed check PR-06 (-20914, UNVERIFIED, legacy text via `OracleErrorCatalog`); reset `PaymentAllocationRules.ResetAfterDiscountChange` |
| T042 | [05_Complex/Inv_Small_Cash.xml:81] | T_INV.AMOUNT_1 · WHEN-VALIDATE-ITEM | `amount_2 := net − amount_1` | DR-07 | `PaymentAllocationRules.AllocateSecondAmount` |
| T043 | [05_Complex/Inv_Small_Cash.xml:84] | T_INV.AMOUNT_2 · WHEN-VALIDATE-ITEM | `REUND` recompute | DR-08 | `PaymentAllocationRules.Refund` |
| T044 | [05_Complex/Inv_Small_Cash.xml:87] | T_INV.SUB_PAYTYPE · WHEN-VALIDATE-ITEM | Payment-type name lookup; its two messages sit in commented code | OI-15.15 | `PAY_TYPE1` LOV display through `LovQueries.PayTypes`; commented code not migrated (D-27) |
| T045 | [05_Complex/Inv_Small_Cash.xml:88] | T_INV.SUB_PAYTYPE · KEY-NEXT-ITEM | Focus navigation | N | UI mechanics: focus navigation |
| T046 | [05_Complex/Inv_Small_Cash.xml:91] | T_INV.SUB_PAYTYPE2 · KEY-NEXT-ITEM | Focus navigation | N | UI mechanics: focus navigation |
| T047 | [05_Complex/Inv_Small_Cash.xml:94] | T_INV.CASH_PAYED · KEY-NEXT-ITEM | Focus navigation | N | UI mechanics: focus navigation |
| T048 | [05_Complex/Inv_Small_Cash.xml:95] | T_INV.CASH_PAYED · WHEN-VALIDATE-ITEM | `NULL` | N | Empty trigger |
| T049 | [05_Complex/Inv_Small_Cash.xml:125] | T_INV.SHOW_RALA · WHEN-BUTTON-PRESSED | Opens `RESERV_NO` LOV | OI-15.18, OI-33 | `LovQueries.ReservNo` (view-only); the chosen `SEQ_NO` is not persistable (OI-33) |
| T050 | [05_Complex/Inv_Small_Cash.xml:143] | T_INV.XRET · WHEN-BUTTON-PRESSED | Canvas switch `MORE` → `CANVAS2` | N | UI mechanics: canvas switch, realised as the Web `App` screen toggle |
| T051 | [05_Complex/Inv_Small_Cash.xml:173] | T_INV.OFERID · KEY-NEXT-ITEM | Focus navigation | N | UI mechanics: focus navigation |
| T052 | [05_Complex/Inv_Small_Cash.xml:559] | D_INV · WHEN-VALIDATE-RECORD | 'This service not requested by doctor at this claim' (warning); cash `PAYRATE 100` | DR-16, PR-04 | `LineEntryRules.WarnNotRequested`; pay rate PR-04 |
| T053 | [05_Complex/Inv_Small_Cash.xml:560] | D_INV · WHEN-NEW-RECORD-INSTANCE | `CHK_SEC_DETAIL` | N, OI-25 | UI mechanics: per-block permission toggling; permissions not built (OI-25) |
| T054 | [05_Complex/Inv_Small_Cash.xml:561] | D_INV · WHEN-CREATE-RECORD | `PAYRATE` default / `DIRECT_COMP_SHARE`; `list_id` | PR-04 | Payer share inside the package calls of preview and create (UNVERIFIED) |
| T055 | [05_Complex/Inv_Small_Cash.xml:562] | D_INV · PRE-INSERT | `D_INV_SEQ` row id | PR-20, OI-40.03 | Engine `insert_lines` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:2794-2968] (UNVERIFIED) |
| T056 | [05_Complex/Inv_Small_Cash.xml:563] | D_INV · POST-QUERY | Insurance employee name from `EMP` | OI-15.27 | `InvoiceQueries.GetMoreDetails` |
| T057 | [05_Complex/Inv_Small_Cash.xml:564] | D_INV · PRE-UPDATE | Update user of a saved line | OI-56 | Saved-line update blocked (D-36) |
| T058 | [05_Complex/Inv_Small_Cash.xml:565] | D_INV · KEY-DELREC | `DELETE_RECORD` on the current line, then `SMALL_CALC` | PR-01, PR-02, PR-03, PR-04, PR-05, PR-06, PR-07, PR-08, OI-56 | Unsaved line: removed from the draft and re-previewed (`InvoiceWorkflowService.Preview`). Saved line of a just-saved invoice: blocked (OI-56, D-36) |
| T059 | [05_Complex/Inv_Small_Cash.xml:566] | D_INV · PRE-DELETE | Request unlink of a deleted saved line | OI-56 | Saved-line deletion blocked (D-36) |
| T060 | [05_Complex/Inv_Small_Cash.xml:567] | D_INV · POST-DELETE | Recalculation and location refresh after a saved-line delete | OI-56 | Saved-line deletion blocked (D-36) |
| T061 | [05_Complex/Inv_Small_Cash.xml:568] | D_INV · POST-INSERT | `PAT_SERV_REQ` link; `T_INV_LOCA` / `D_INV_LOCA` service locations; `PACKAGE_CONS_M` / `PACKAGE_CONS` | PR-20, OI-09, OI-31 | Link PR-20 inside `CreateFullInvoice`; locations → queue posting (OI-09, D-14, UNVERIFIED); package consumption blocked (OI-31) |
| T062 | [05_Complex/Inv_Small_Cash.xml:569] | D_INV · KEY-CREREC | New lines on a saved invoice while `UPDATE_ALLOWED` is true | OI-56, DR-17 | Blocked on a saved invoice (D-36); unsaved drafts add lines freely (`InvoiceStatePolicy.CanEdit`) |
| T063 | [05_Complex/Inv_Small_Cash.xml:570] | D_INV · POST-UPDATE | Location refresh after a saved-line update | OI-56 | Saved-line update blocked (D-36) |
| T064 | [05_Complex/Inv_Small_Cash.xml:384] | D_INV.CATID · KEY-NEXT-ITEM | Navigation | N | UI mechanics: focus navigation |
| T065 | [05_Complex/Inv_Small_Cash.xml:388] | D_INV.SERVICEID · KEY-NEXT-ITEM | Navigation; `AMOUNT_1 := net` after the last line | DR-07 | `PaymentAllocationRules.DefaultFirstAmount`; navigation is UI mechanics |
| T066 | [05_Complex/Inv_Small_Cash.xml:389] | D_INV.SERVICEID · WHEN-VALIDATE-ITEM | 'You Must Select Value'; inline copy of OKA (eligibility, revisit and claim checks, advance payment, cash card, package consumption); its own standard-offer branch (cash: `OFFERS` / `OFFERS_DTL` with `OFFER_TYPE=1` set price and discount), which the `OKA` unit lacks; `CHK_ADV_CLASS`; `PRICE` enablement; pay rate; `SMALL_CALC` | DR-15, DR-23, PR-16, PR-25, PR-04, PR-01, OI-32, OI-31 | `LineEntryRules.RequireService`; `AddToListRule.Derive`; standard-offer branch → PR-16, PR-25 inside the package (UNVERIFIED); share PR-04; untraced sub-rules → `OpenItemGate.Evaluate` blockers (OI-32); `PRICE` editability → price-override binding (D-42); consumption blocked (OI-31); recalculation PR-01 … PR-08 via `InvoiceWorkflowService.Preview` |
| T067 | [05_Complex/Inv_Small_Cash.xml:393] | D_INV.PRICE · WHEN-VALIDATE-ITEM | `SMALL_CALC` | PR-01 | Re-preview through `BilInvoiceApiGateway.CalculatePreview` |
| T068 | [05_Complex/Inv_Small_Cash.xml:396] | D_INV.QTY · WHEN-VALIDATE-ITEM | `SHOW_QTY` limit; quantity ≥ 1; remaining package quantity | DR-12, OI-31 | `LineEntryRules.ValidateQuantity`; remaining quantity blocked (OI-31) |
| T069 | [05_Complex/Inv_Small_Cash.xml:397] | D_INV.QTY · KEY-NEXT-ITEM | Navigation | N | UI mechanics: focus navigation |
| T070 | [05_Complex/Inv_Small_Cash.xml:400] | D_INV.DISC · WHEN-VALIDATE-ITEM | `SMALL_CALC` | PR-02 | Re-preview through `BilInvoiceApiGateway.CalculatePreview` |
| T071 | [05_Complex/Inv_Small_Cash.xml:403] | D_INV.MY_DISC · WHEN-VALIDATE-ITEM | `SMALL_CALC` | PR-02 | Re-preview through `BilInvoiceApiGateway.CalculatePreview` |
| T072 | [05_Complex/Inv_Small_Cash.xml:406] | D_INV.FIXPAY · WHEN-VALIDATE-ITEM | Mutual reset + recalculation | PR-04, OI-33 | Payer share inside the package (UNVERIFIED); operator override not carried (OI-33) |
| T073 | [05_Complex/Inv_Small_Cash.xml:409] | D_INV.PAYRATE · WHEN-VALIDATE-ITEM | Mutual reset + recalculation | PR-04, OI-33 | Payer share inside the package (UNVERIFIED); operator override not carried (OI-33) |
| T074 | [05_Complex/Inv_Small_Cash.xml:439] | D_INV.APPROV_REF_NO · WHEN-VALIDATE-ITEM | Credit line needing approval without a reference | DR-14 | `LineEntryRules.ValidateApproval` |
| T075 | [05_Complex/Inv_Small_Cash.xml:713] | T_INV_TRANS_M · PRE-QUERY | `inv_type := 197` | OI-15.03 | `INV_TYPE = '197'` filter in `InvoiceQueries.GetMoreDetails` |
| T076 | [05_Complex/Inv_Small_Cash.xml:721] | TOOL.DO_PREVIEW · WHEN-CHECKBOX-CHANGED | Print-preview flag | N, OI-11 | UI mechanics: print flag; printing blocked (OI-11, OI-45, OI-46) |
| T077 | [05_Complex/Inv_Small_Cash.xml:727] | TOOL.LDISCT · WHEN-RADIO-CHANGED | No value discount on credit invoices | DR-13 | `LineEntryRules.ValidateDiscountType` |
| T078 | [05_Complex/Inv_Small_Cash.xml:728] | TOOL.LDISCT · WHEN-VALIDATE-ITEM | No value discount on credit invoices | DR-13 | `LineEntryRules.ValidateDiscountType` |
| T079 | [05_Complex/Inv_Small_Cash.xml:737] | TOOL.DO_PRINT · WHEN-BUTTON-PRESSED | `YES_NO` alert → commit (+ print); the record stays in the block after commit | PR-22, PR-23, OI-11, OI-45, OI-46, OI-49, OI-56 | `InvoicesController.Create` → `InvoiceWorkflowService.Create`; print part blocked (`InvoicesController.Documents`, OI-11, OI-45, OI-46, OI-49); post-save edit blocked (OI-56) |
| T080 | [05_Complex/Inv_Small_Cash.xml:740] | TOOL.PUSH_BUTTON1161 · WHEN-BUTTON-PRESSED | `PAT_CARD_INV.jsp` | OI-47 | Blocked: `LegacyExternalCalls.BuildLegacyDocument` (`documents/patient-card` → 501) |
| T081 | [05_Complex/Inv_Small_Cash.xml:743] | TOOL.PUSH_BUTTON1199 · WHEN-BUTTON-PRESSED | Cash-invoice SMS via `SEND_MESSAG` carrying an `inv_small_cash.jsp` link | OI-12, OI-26, OI-45 | Blocked: `InvoicesController.Sms` → 501 (D-28) |
| T082 | [05_Complex/Inv_Small_Cash.xml:746] | TOOL.GO_LAST · WHEN-BUTTON-PRESSED | `MAX(INV_NO)` where `IN_OUT=1`, `ROW_TYPE=1`, `PHARMACY_INV_NO IS NULL`, current information centre | OI-15.01 | `InvoiceQueries.GetLastInvoiceNo` (same predicates) through `InvoicesController.Last` |
| T083 | [05_Complex/Inv_Small_Cash.xml:749] | TOOL.RELASE · WHEN-BUTTON-PRESSED | `PAYED_BEFORE := 0`, recalculation | OI-23 | Not built: `GET_PAYID_VALUE` is missing, so Release is not rendered |
| T084 | [05_Complex/Inv_Small_Cash.xml:752] | TOOL.PUSH_BUTTON1062 · WHEN-BUTTON-PRESSED | Open `phy_req_note` | OI-50 | Not built: other Form, out of scope |
| T085 | [05_Complex/Inv_Small_Cash.xml:755] | TOOL.DO_IMP2 · WHEN-BUTTON-PRESSED | Request import of `V_SERVICES_REQ` rows with `SELECT_TO_INV=1` and `D_INV_ROW_ID IS NULL`; rejected rows → serviceid \|\| ' Rejected ', need-approval rows → serviceid \|\| ' Need Approval'; `X422_APPROV_CHECK=2` bypasses approval; imported cash lines take the standard offer's price and discount (T066's branch, repeated) | DR-18, PR-19, PR-16, OI-15.08 | `RequestImportRules.RequireDoctor`, `RequestImportRules.Notices` (D-26); `InvoiceQueries.GetSelectedRequestRows`; `BilImportGateway.ImportRequestLines` with approval mode 0 / 1 (D-12); standard offer on imported cash lines → PR-16 inside the package at preview and create (UNVERIFIED) |
| T086 | [05_Complex/Inv_Small_Cash.xml:758] | TOOL.PUSH_BUTTON934 · WHEN-BUTTON-PRESSED | Canvas switch `CANVAS2` → `MORE` | N | UI mechanics: canvas switch, realised as the Web `App` screen toggle |
| T087 | [05_Complex/Inv_Small_Cash.xml:761] | TOOL.CMD_LANG · WHEN-BUTTON-PRESSED | Language toggle | N | UI mechanics: single-language UI (D-18) |
| T088 | [05_Complex/Inv_Small_Cash.xml:764] | TOOL.DO_TRANSLATE · WHEN-BUTTON-PRESSED | Open `translate` | OI-51 | Not built: other Form, out of scope |
| T089 | [05_Complex/Inv_Small_Cash.xml:771] | TOOL.IMP_PKG · WHEN-BUTTON-PRESSED | Package components into lines; 'No Serves Added' as a message without `FORM_TRIGGER_FAILURE` | PR-18, DR-19, DR-23 | `BilInvoiceApiGateway.GetPackageLines` (price list per D-38); `PackageImportRules.Evaluate` (warning); `AddToListRule.Derive` |
| T090 | [05_Complex/Inv_Small_Cash.xml:774] | TOOL.ITEM1114 · WHEN-BUTTON-PRESSED | Open `st_trans` | OI-52 | Not built: other Form, out of scope |
| T091 | [05_Complex/Inv_Small_Cash.xml:777] | TOOL.PUSH_BUTTON1123 · WHEN-BUTTON-PRESSED | Operator store transfer on a saved invoice: rewrites `TRANS_M` / `TRANS_DTL`, then `SILENT_COMMET00` | OI-10, OI-44 | Blocked: `InvoicesController.StockTransfer` → 501 (`LegacyExternalCalls.TransferStock`, D-46) |
| T092 | [05_Complex/Inv_Small_Cash.xml:780] | TOOL.ITEM1124 · WHEN-BUTTON-PRESSED | Open `INVOICE_PAYMENT` | OI-28 | Not built: other Form, out of scope |
| T093 | [05_Complex/Inv_Small_Cash.xml:783] | TOOL.ITEM1151 · WHEN-BUTTON-PRESSED | Offer detail lines into the invoice | PR-17 | `BilInvoiceApiGateway.GetBundledOfferLines` through `ImportsController.BundledOffer` |
| T094 | [05_Complex/Inv_Small_Cash.xml:786] | TOOL.P_PRINT · WHEN-BUTTON-PRESSED | `iqama_check.jsp` | OI-48 | Blocked: `LegacyExternalCalls.BuildLegacyDocument` (`documents/iqama-check` → 501) |
| T095 | [05_Complex/Inv_Small_Cash.xml:789] | TOOL.ITEM1200 · WHEN-BUTTON-PRESSED | `PAT_CARD_INV.jsp` + barcode SMS | OI-47, OI-26 | Blocked: `LegacyExternalCalls.BuildLegacyDocument` (`documents/barcode-sms` → 501) |

**Program units (30)**

| Id | Locator | Owner · event | Business content | Class | Target member or reason |
|---|---|---|---|---|---|
| PU01 | [05_Complex/Inv_Small_Cash.xml:960] | `CHK_SEC_DETAIL` · program unit | Per-block permission toggling | N, OI-25 | UI mechanics: item toggling; permissions not built (OI-25) |
| PU02 | [05_Complex/Inv_Small_Cash.xml:961] | `CHK_VOL` · program unit | Text_IO / Host serial check; never called | N | Dead code: never called (client-machine behaviour) |
| PU03 | [05_Complex/Inv_Small_Cash.xml:962] | `DO_INTERFACE` · program unit | UI direction and prompts | N | UI mechanics: layout direction |
| PU04 | [05_Complex/Inv_Small_Cash.xml:963] | `DISABLE_AN_ITEM` · program unit | Item disabling | N | UI mechanics: item enablement |
| PU05 | [05_Complex/Inv_Small_Cash.xml:964] | `HIDE_AN_ITEM` · program unit | Item hiding | N | UI mechanics: item visibility |
| PU06 | [05_Complex/Inv_Small_Cash.xml:965] | `CHK_TIME` · program unit | Time check; never called | N | Dead code: never called |
| PU07 | [05_Complex/Inv_Small_Cash.xml:966] | `CHK_SEC` · program unit | Permissions via `GET_U_PREV20` | OI-25 | Not built: no authorisation in this run |
| PU08 | [05_Complex/Inv_Small_Cash.xml:967] | `MESSAG` · program unit | Shows `ERR_ALERT` | N | UI mechanics: alert display, replaced by the error contract (`ProblemDetailsWriter.WriteAsync`, `MessageDto`) |
| PU09 | [05_Complex/Inv_Small_Cash.xml:968] | `HIDE_AN_ITEM2` · program unit | Item hiding; never called | N | Dead code: never called |
| PU10 | [05_Complex/Inv_Small_Cash.xml:969] | `SMALL_CALC` · program unit | Line and total calculation, deductible cap, VAT, `amount_1 := net` | PR-01, PR-02, PR-03, PR-04, PR-05, PR-06, PR-07, PR-08, OI-23 | `BilInvoiceApiGateway.CalculatePreview`; the `payed_before` part of the cap is blocked (OI-23, D-51) |
| PU11 | [05_Complex/Inv_Small_Cash.xml:970] | `IMP_RXXX` · program unit | Patient-transfer import; never called | N | Dead code: never called |
| PU12 | [05_Complex/Inv_Small_Cash.xml:971] | `CHECK_PACKAGE_FAILURE` · program unit | Relation coordination | N | UI mechanics: master-detail coordination |
| PU13 | [05_Complex/Inv_Small_Cash.xml:972] | `QUERY_MASTER_DETAILS` · program unit | Relation coordination | N | UI mechanics: master-detail coordination |
| PU14 | [05_Complex/Inv_Small_Cash.xml:973] | `CLEAR_ALL_MASTER_DETAILS` · program unit | Relation coordination | N | UI mechanics: master-detail coordination |
| PU15 | [05_Complex/Inv_Small_Cash.xml:974] | `NEW_WATING_NOXXXXX` · program unit | Waiting-number variant; never called | N | Dead code: never called |
| PU16 | [05_Complex/Inv_Small_Cash.xml:975] | `MAKE_CASH` · program unit | Cash payer context (company '0', `GET_PRICE_PLAN`, direct share) | DR-24, PR-24, OI-03, OI-24 | Pay type `PayTypeSelectionRule.Decide`; payer context PR-24 (OI-03); price plan not built (OI-24) |
| PU17 | [05_Complex/Inv_Small_Cash.xml:976] | `DO_DISC` · program unit | Line discount rate ↔ value | PR-02, DR-13 | Engine discount calculation (UNVERIFIED); its message is covered by `LineEntryRules.ValidateDiscountType` |
| PU18 | [05_Complex/Inv_Small_Cash.xml:977] | `GET_NOTES` · program unit | Request notes for location rows | OI-09 | Queue posting stage (UNVERIFIED) |
| PU19 | [05_Complex/Inv_Small_Cash.xml:978] | `OKA` · program unit | Service admission checks and pricing context; no offer branch | PR-03, PR-04, DR-23, OI-31, OI-32 | Package pricing context: discountability PR-03 and payer share PR-04 inside the package (UNVERIFIED); `AddToListRule.Derive` (D-37); untraced sub-rules → `OpenItemGate.Evaluate` blockers (OI-32); consumption blocked (OI-31); no offer branch: standard offers are traced to T066 and T085 |
| PU20 | [05_Complex/Inv_Small_Cash.xml:979] | `ROUND_FOR_CASH` · program unit | Patient amount due | PR-07 | Engine `cash_collected` (UNVERIFIED) |
| PU21 | [05_Complex/Inv_Small_Cash.xml:980] | `CHG_PRMPT2` · program unit | Prompt translation via `FIND_PROMPT` | N, OI-43 | UI mechanics: prompt translation |
| PU22 | [05_Complex/Inv_Small_Cash.xml:981] | `CHK_LIC_SEC` · program unit | Licence check; never called | N | Dead code: never called |
| PU23 | [05_Complex/Inv_Small_Cash.xml:982] | `CHK_ADV_CLASS` · program unit | Class-based share, advanced class rules, cash-card line discount | PR-04, OI-23, OI-32 | Share and advanced classes PR-04; advanced-class deductible blocked (OI-23, D-51); cash card blocked (OI-32) |
| PU24 | [05_Complex/Inv_Small_Cash.xml:983] | `A_HIDE_SHOW` · program unit | Item visibility | N | UI mechanics: item visibility |
| PU25 | [05_Complex/Inv_Small_Cash.xml:984] | `DO_PRINT` · program unit | Report URL (`inv_small_cash.jsp` or `inv_form2.jsp`, `LIST1111.jsp`), PDF, printer | OI-11, OI-45, OI-46, OI-49 | Blocked: `BilInvoiceApiGateway.BuildPrintUrl`; PDF export and printer routing not migrated (client-machine behaviour) |
| PU26 | [05_Complex/Inv_Small_Cash.xml:985] | `REF_TIME` · program unit | Reservation time display via `GET_HTFN2` | OI-42 | Not built: display field omitted |
| PU27 | [05_Complex/Inv_Small_Cash.xml:986] | `CAN_GO_ITEM` · program unit | Item check comparing against 'TEUE'; never called | N | Dead code: never called |
| PU28 | [05_Complex/Inv_Small_Cash.xml:987] | `DO_NEW_RECORD` · program unit | New record; never called | N | Dead code: never called |
| PU29 | [05_Complex/Inv_Small_Cash.xml:988] | `PRINT_URL` · program unit | Report URL; never called | N | Dead code: never called |
| PU30 | [05_Complex/Inv_Small_Cash.xml:989] | `REMAIN` · program unit | Refund to the patient | DR-08 | `PaymentAllocationRules.Refund` |

## §4 Program-unit-to-package mapping

None of these units is re-implemented in C#. Each is reached only through the retained package call named, all UNVERIFIED.

| Program unit | Package operation that now owns it | Notes |
|---|---|---|
| `SMALL_CALC` (PU10) [05_Complex/Inv_Small_Cash.xml:969] | `BIL_INVOICE_API.CALCULATE_EDITABLE_INVOICE_PREVIEW` → `BIL_INVOICE_ENGINE.PREVIEW_INVOICE` / `calculate_lines` (PR-01 … PR-08, PR-15), through `BilInvoiceApiGateway.CalculatePreview` | Legacy credit deductible cap `MAX_DEDUCTABLE − (payed_before + s_pay)`. The running `s_pay` part corresponds to the engine's `p_patient_paid_so_far` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:2023] (PR-04, UNVERIFIED). The `payed_before` part needs the missing `GET_PAYID_VALUE` and has no package counterpart, so credit preview and create block with OI-23 wherever the cap can apply (D-51) |
| `DO_DISC` (PU17) [05_Complex/Inv_Small_Cash.xml:976] | Engine `calc_discount_amount` / `calc_discount_percent` (PR-02) | Its message 'use Discount rate for insurance companys' is covered by DR-13 |
| `CHK_ADV_CLASS` (PU23) [05_Complex/Inv_Small_Cash.xml:982] | Engine `calculate_lines` → `bil_class_rule.resolve_share` (PR-04) | Advanced classes (`DISC_CLASSES.USE_ADVANCED`, `COMPANY_CAT`, `SYS_CAT_TYPE`) are payer share (OI-06, UNVERIFIED). An advanced class can set `MAX_DEDUCTABLE` per line, which .NET does not derive, so a credit draft with one blocks under OI-23 (D-51). The cash-card branch has no package trace and blocks (OI-32) |
| `OKA` (PU19, inline copy in T066) [05_Complex/Inv_Small_Cash.xml:978] | Engine `calculate_lines`, `bil_service_context`, `BIL_IMPORT` (PR-03, PR-04) | Revisit limits, claim count / `ONE_VISIT_960`, advance payment `PKG_TYPE=3`, cash-card discount and begin-of-claim eligibility have no trace in the three sources; each blocks the operation that needs it when its condition holds (OI-32, D-05). `ADD_TO_LIST` is DR-23 (D-37); package consumption is OI-31. `OKA` has no offer branch: the standard-offer branch sits beside the inline copy in T066 [05_Complex/Inv_Small_Cash.xml:389] and T085 repeats it [05_Complex/Inv_Small_Cash.xml:755]; it maps to `bil_offer_rule` (PR-16, PR-25), not to this unit |
| `ROUND_FOR_CASH` (PU20) [05_Complex/Inv_Small_Cash.xml:979] | Engine `cash_collected` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:3058-3059] (PR-07) | The legacy credit branch `S_PAY + nvl(VAT_TOTAL_PAT,2)` is unrounded and defaults VAT to 2; the package governs (§10, D-40) |
| `GET_NEXT_INVOICE_NO` | Engine `get_next_invoice_no_safe` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1082-1102] calling the standalone function (PR-09) | A standalone DB function [05_Complex/README.md:357], not a Form program unit; T011 is its Form caller (D-02, OI-19) |

## §5 Domain rule catalogue

**Named-unit sub-rules (D-05).** The logic of `OKA` and `CHK_ADV_CLASS` is package-owned, but some sub-rules have no trace in the three sources. The target neither re-codes them nor skips them: `OpenItemGate.Evaluate` detects their conditions from server-read values only (D-52), and the operation that needs them returns 501.

| Sub-rule [Form locator] | Package evidence | Target |
|---|---|---|
| Payer share, advanced classes (`DISC_CLASSES.USE_ADVANCED`, `COMPANY_CAT`), `DIRECT_COMP_SHARE` line rate [05_Complex/Inv_Small_Cash.xml:978, 982] | `bil_class_rule.resolve_share` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:2016-2025] | PR-04, UNVERIFIED (OI-06); the prior-payment part of the deductible cap is OI-23 (D-51) |
| Standard offers: T066's own branch, repeated by T085 for imported request lines; the `OKA` unit has none [05_Complex/Inv_Small_Cash.xml:389, 755] | `bil_offer_rule` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1866-1885] | PR-16, PR-25, UNVERIFIED (OI-07) |
| Eligibility before queueing: `ADD_TO_QUE=1`, sub-company set, `PRE_AUTHORIZATION` null → 'Elligabilty should be done with services marked as begin of claim' [05_Complex/Inv_Small_Cash.xml:389, 978] | None: `pre_authorization` is stored, never checked [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:2708, 2756] (a reading of the package source; runtime behaviour UNVERIFIED) | Blocked (OI-32) whenever a line's service (or a package component) has `ADD_TO_QUE=1` and the header has a sub-company; no request pre-authorisation is read (D-52) |
| Claim and revisit limits: `CONS_REV` 1 / 2, `ONE_VISIT_960` [05_Complex/Inv_Small_Cash.xml:389, 978] | None | Blocked (OI-32) when the service has `CONS_REV` 1 or 2 |
| Advance-payment instalments: `IS_PACKAGE=1` with `PKG_TYPE=3` [05_Complex/Inv_Small_Cash.xml:389, 978] | None | Blocked (OI-32) |
| Cash-card line discount from `CASH_CARD_DISCDTL` when `CARD_ID` is set [05_Complex/Inv_Small_Cash.xml:43, 978, 982] | None | Blocked (OI-32) when the server-read card id (`LookupQueries.GetPatientCardId`, claim preload) is set (D-52) |
| `ADD_TO_LIST` flag [05_Complex/Inv_Small_Cash.xml:43, 389, 978] | Caller-supplied `add_to_list`, stored and never derived [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:21, 2782] (a reading of the package source; runtime behaviour UNVERIFIED) | DR-23 (D-37) |
| Package consumption (`PKG_INV`) [05_Complex/Inv_Small_Cash.xml:389, 978] | None | Blocked (OI-31, D-30) |

**Legacy post-insert side effects.** Each is assigned to a `CREATE_FULL_INVOICE` posting stage [05_Complex/APEX_Reference/backend/README.md:145-158] whose implementation is in a missing package; .NET writes none of these tables (D-14). Every equivalence is UNVERIFIED.

| Legacy side effect [locator] | Posting stage | Status |
|---|---|---|
| Visit list `PAT_VISIT_M` (T016 [05_Complex/Inv_Small_Cash.xml:368]); queue number `SEQ_NO` / `DOC_SEQ` (T011 [05_Complex/Inv_Small_Cash.xml:363]); service locations `T_INV_LOCA` / `D_INV_LOCA` (T061 [05_Complex/Inv_Small_Cash.xml:568]) | `BIL_QUEUE_POSTING.post_invoice_queue` [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:1405-1414] | UNVERIFIED (OI-09) |
| Store transfer, T091 [05_Complex/Inv_Small_Cash.xml:777] | `BIL_STOCK_POSTING.post_invoice_stock` at create [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:1416-1424] | Not equated; T091 is a blocked action (OI-10, OI-44, D-46) |
| Audit `HMISFOX_AUDIT` (T016) | `BIL_AUDIT` in engine create [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:3252, 3264] | UNVERIFIED (OI-13) |
| Cash SMS with an `inv_small_cash.jsp` link (T016) | `BIL_MESSAGE` | Not sent (OI-12, OI-45, D-28) |
| Payment | `BIL_PAYMENT.post_payment` [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:1385-1403] | UNVERIFIED (OI-08, D-13) |
| Print | `BIL_REPORTS_PRINT` | Blocked (OI-11) |

**Domain rules DR-01 … DR-25.** Legacy texts are copied byte for byte from the XML, including spelling, double and trailing spaces; `\|\|` shows concatenation. The `Legacy text (verbatim)` column holds only texts copied from the XML; a text the target emits that the XML does not hold (DR-03's inferred Forms diagnostic, DR-06's DISC_T message) is stated in the Rule column and marked non-legacy. Target members are in `Billing.Invoicing.Domain.Rules` or `.Workflow`. Test classes are in `tests/Billing.Invoicing.Tests/Domain` (`Category=DomainParity`); fixtures in `Parity/fixtures`.

| DR | Source [locator] | Rule | Legacy text (verbatim) | Severity | Target member | Test class · fixture |
|---|---|---|---|---|---|---|
| DR-01 | T014 [05_Complex/Inv_Small_Cash.xml:366] | Patient required; credit (`PAYTYPE=2`) with company '0' refused; doctor required (the block WHERE makes `PHARMACY_INV_NO` null, so it always applies) | `'Select Patient No is required '`; `'this patient not belong to any company !!'`; `'Doctor No is required '` | Blocking | `HeaderRecordRules.ValidateRecord` | `HeaderRecordRulesTests` · `DR-01.json` |
| DR-02 | T009 [05_Complex/Inv_Small_Cash.xml:1102] | No lines refused | `'Invoice without Details'` | Blocking | `InvoiceDetailRules.RequireDetails` | `InvoiceDetailRulesTests` · `DR-02.json` |
| DR-03 | T023 [05_Complex/Inv_Small_Cash.xml:18] | Coverage validity from the `V_PAT_DATA` snapshot; blocking vs warning follows the `INV_DATE_ADMIN` and company-type branches. Non-legacy: a nonblank patient with no `V_PAT_DATA` row hits T023's unhandled ORA-01403 (its `NO_DATA_FOUND` handler is commented out); the target returns one blocking `PATIENTNO` message with the inferred Forms runtime diagnostic 'FRM-40735: WHEN-VALIDATE-ITEM trigger raised unhandled exception ORA-01403.', which is not an XML text; its exact Forms wording is UNVERIFIED (not observed at runtime) (D-72) | `'Contract  Ended '\|\| to_char(r1.cCONTEND,'dd/mm/yyyy')` (\|\| `' , But due to that user have date admin privileges system will open claim'`); `'Company Is Holed'`; `'Card Expired '\|\| to_char(r1.CARD_END,'dd/mm/yyyy')` (\|\| `' , But due to that user have date admin privileges system will open claim'` / `' , Today last Date'`); `'Policy  Ended '` \|\| date \|\| `' ,But due to that user have date admin privileges system will open claim'`; `'Policy  Ended '` \|\| date \|\| `' Patient well treated as cash patient '`; `'Policy Is Holed'`; `'Refral Required For This Class'`; `'Class Is Holed'` | Blocking or warning per branch | `PatientEligibilityRules.Evaluate` | `PatientEligibilityRulesTests` · `DR-03.json` |
| DR-04 | T031 [05_Complex/Inv_Small_Cash.xml:47] | Clinic sex vs patient sex; age check needs `DAY_TO_DAYES` (OI-22) | `'Patient sex not suitable for this clinic'`; `'Patient age  not suitable for this clinic'` | Warning | `ClinicSuitabilityRules.CheckSex`, `ClinicSuitabilityRules.CheckAge` | `ClinicSuitabilityRulesTests` · `DR-04.json` |
| DR-05 | T032 [05_Complex/Inv_Small_Cash.xml:50], T033 [05_Complex/Inv_Small_Cash.xml:53] | `NVL(DEPT_WISE,0)=1` at a clinic whose `SYS_CAT_TYPE` is not 'ER'; `CALL=1` alone never rejects | `'This open for GP at ER Clinic Only'` | Blocking | `ErClinicRule.Validate` | `ErClinicRuleTests` · `DR-05.json` |
| DR-06 | T039 [05_Complex/Inv_Small_Cash.xml:74], T041 [05_Complex/Inv_Small_Cash.xml:78] | With no header offer, the final-discount percent (entered, or `round(finaldisc/pat_pay*100,2)` in value mode) above `USERS_TABLE.MAX_DISC` opens `DISC_ALERT`; "Maximum Discount" caps, "Cancel" zeroes and blocks; the amount is the package's (D-07, D-41). Non-legacy: a `DISC_T` other than null (value mode), 0 or 1 is refused with one blocking `DISC_T` message carrying the target text 'DISC_T must be 0 (Value Disc) or 1 (Rate Disc)', which is not an XML text (D-97) | `'Maximum discount allawed is' \|\| V_MAX_DISC` | Blocking unless "Maximum Discount" | `FinalDiscountLimitRule.Evaluate`, `FinalDiscountLimitRule.ApplyChoice` | `FinalDiscountLimitRuleTests` · `DR-06.json` |
| DR-07 | T039 [05_Complex/Inv_Small_Cash.xml:74], T042 [05_Complex/Inv_Small_Cash.xml:81], T065 [05_Complex/Inv_Small_Cash.xml:388] | Allocation of the amount due between the two payment methods; the amount due is the package's `cash_collected` (D-40) | — | Value rule | `PaymentAllocationRules.AllocateSecondAmount`, `.ResetAfterDiscountChange`, `.DefaultFirstAmount` | `PaymentAllocationRulesTests` · `DR-07.json` |
| DR-08 | PU30 [05_Complex/Inv_Small_Cash.xml:989], T043 [05_Complex/Inv_Small_Cash.xml:84] | Refund = `CASH_PAYED` − cash-method amounts when both are > 0, else 0 | — | Value rule | `PaymentAllocationRules.Refund` | `PaymentAllocationRulesTests` · `DR-08.json` |
| DR-09 | `CASH_COLLECTED` formula [05_Complex/Inv_Small_Cash.xml:97] | Total collected = amount 1 + amount 2 | — | Value rule | `PaymentAllocationRules.TotalCollected` | `PaymentAllocationRulesTests` · `DR-09.json` |
| DR-10 | T029 [05_Complex/Inv_Small_Cash.xml:43] | Claim number from the parameter when `CLAIM_FLAG='R'` or the claim parameter is not '1' / '2'; else built from patient, clinic and draft date | `'O'  \|\|'-'\|\| :patientno \|\|'-'\|\| :clinicid \|\|'-'\|\| to_char(:invdate,'ddmmyy')` | Value rule | `ClaimNumberRule.Build` | `ClaimNumberRuleTests` · `DR-10.json` |
| DR-11 | T029 [05_Complex/Inv_Small_Cash.xml:43] | Doctor missing; doctor differs from `PARAMETER.THE_DOC` (reset to it) | `'You Must Select Doctor'`; `'You Cant Change doctor'` | Warning (raise swallowed) | `DoctorSelectionRules.Validate` | `DoctorSelectionRulesTests` · `DR-11.json` |
| DR-12 | T068 [05_Complex/Inv_Small_Cash.xml:396] | `SHOW_QTY=1` with qty > 1; qty ≤ 0 | `'Due to Quality system not allow more than 1 at qty for this service'`; `'Qty should be >=1'` | Blocking | `LineEntryRules.ValidateQuantity` | `LineEntryRulesTests` · `DR-12.json` |
| DR-13 | T077 [05_Complex/Inv_Small_Cash.xml:727], T078 [05_Complex/Inv_Small_Cash.xml:728] | Value discount (`V`) with a class code; type resets to `R` | `'You cant use value disocunt for credit invoices'` | Blocking | `LineEntryRules.ValidateDiscountType` | `LineEntryRulesTests` · `DR-13.json` |
| DR-14 | T074 [05_Complex/Inv_Small_Cash.xml:439] | Credit, `X422_APPROV_CHECK=1`, `REQ_NEED_A<>0`, no `APPROV_REF_NO` | `:serviceid \|\| 'Need Approval'` | Blocking | `LineEntryRules.ValidateApproval` | `LineEntryRulesTests` · `DR-14.json` |
| DR-15 | T066 [05_Complex/Inv_Small_Cash.xml:389] | Service missing | `'You Must Select Value'` | Blocking | `LineEntryRules.RequireService` | `LineEntryRulesTests` · `DR-15.json` |
| DR-16 | T052 [05_Complex/Inv_Small_Cash.xml:559] | Insured, new invoice, `BEGIN_OF_CLAIM=0`, no matching `PAT_SERV_REQ` | `'This service not requested by doctor at this claim'` | Warning | `LineEntryRules.WarnNotRequested` | `LineEntryRulesTests` · `DR-16.json` |
| DR-17 | T010 [05_Complex/Inv_Small_Cash.xml:362], T020 [05_Complex/Inv_Small_Cash.xml:372], T021 [05_Complex/Inv_Small_Cash.xml:373], T062 [05_Complex/Inv_Small_Cash.xml:569] | Legacy Form: T010 POST-QUERY sets `UPDATE_ALLOWED` and `DELETE_ALLOWED` false on `T_INV` and `D_INV`, so only a queried invoice is read-only [05_Complex/Inv_Small_Cash.xml:362]; T079 commits without clearing the record [05_Complex/Inv_Small_Cash.xml:737], so a just-saved invoice stays editable, and T062 adds lines to it while `UPDATE_ALLOWED` is true [05_Complex/Inv_Small_Cash.xml:569]; delete is refused (T020, T021); unsaved drafts take lines freely. Target: `InvoiceStatePolicy.CanEdit` is true only for an unsaved draft, so every saved invoice, queried or just saved, is read-only; the just-saved block is the target's policy, not the Form's (OI-56, D-36) | `'You Cant Delete Invoice From Here'` | Blocking | `InvoiceStatePolicy.CanEdit`, `InvoiceStatePolicy.CanDelete` | `InvoiceStatePolicyTests` · `DR-17.json` |
| DR-18 | T085 [05_Complex/Inv_Small_Cash.xml:755] | Import needs a doctor; per-service notices from the selected rows' own flags; `BIL_IMPORT` decides the rows (PR-19, D-26) | `'Select doctor First'`; `r1.serviceid \|\| ' Rejected '`; `r1.serviceid \|\| ' Need Approval'` | Blocking (doctor); notices | `RequestImportRules.RequireDoctor`, `RequestImportRules.Notices` | `RequestImportRulesTests` · `DR-18.json` |
| DR-19 | T089 [05_Complex/Inv_Small_Cash.xml:771] | Package import returning zero lines | `'No Serves Added'` | Warning | `PackageImportRules.Evaluate` | `PackageImportRulesTests` · `DR-19.json` |
| DR-20 | T015 [05_Complex/Inv_Small_Cash.xml:367], T022 [05_Complex/Inv_Small_Cash.xml:13], T003 [05_Complex/Inv_Small_Cash.xml:1096] | New-draft defaults: home care → type 7; `SUB_PAYTYPE := 1`; draft date = database `SYSDATE` (D-39); claim preload; visit doctor | — | Value rule | `InvoiceDefaultsRule.Apply` | `InvoiceDefaultsRuleTests` · `DR-20.json` |
| DR-21 | T011 [05_Complex/Inv_Small_Cash.xml:363] | After a new create with `NEW_INV_DOCID` set, clear the four `NEW_INV_*` columns; skipped on a replay; failure swallowed (D-44) | — | Value rule | `ReceptionTransferRule.ShouldClear` (+ `PatientTransferCommand.ClearReceptionTransfer`) | `ReceptionTransferRuleTests` · `DR-21.json` |
| DR-22 | T014 [05_Complex/Inv_Small_Cash.xml:366], T038 [05_Complex/Inv_Small_Cash.xml:73], T040 [05_Complex/Inv_Small_Cash.xml:77] | Collected > 0 with both payment methods empty → `SUB_PAYTYPE := 1` | `'Payment type is empty'` | Warning | `HeaderRecordRules.ApplyPaymentTypeDefault` | `HeaderRecordRulesTests` · `DR-22.json` |
| DR-23 | T029 [05_Complex/Inv_Small_Cash.xml:43], T066 [05_Complex/Inv_Small_Cash.xml:389] | `ADD_TO_LIST := 1` for a server-read `ADD_TO_QUE=1` service, or a `SERV_LOC_ID=14` package with such a component (D-37) | — | Value rule | `AddToListRule.Derive` | `AddToListRuleTests` · `DR-23.json` |
| DR-24 | T015 [05_Complex/Inv_Small_Cash.xml:367], T023 [05_Complex/Inv_Small_Cash.xml:18], T026 [05_Complex/Inv_Small_Cash.xml:27] | Pay-type entry decision bound as `T_HEADER_INPUT.paytype` (D-47); a claim preload's null pay type is inherited and refused wherever it must be bound (D-88) | — | Value rule | `PayTypeSelectionRule.Decide` | `PayTypeSelectionRuleTests` · `DR-24.json` |
| DR-25 | T029 [05_Complex/Inv_Small_Cash.xml:43] | Automatic visit line: review, consultation, service '2000' for company '1059' at clinic 14, or none (D-47) | — | Value rule | `VisitLineRule.Choose` | `VisitLineRuleTests` · `DR-25.json` |

## §6 Package-resident rules — UNVERIFIED

Every rule below is reached only through a retained PL/SQL call and is UNVERIFIED: no Oracle instance exists in this run. Each has one `[OracleFact]` test in `tests/Billing.Invoicing.Tests/Oracle/PackageParityTests.cs` (`Category=OracleParity`), driven by `Parity/fixtures/PR-xx.json`. Case statuses follow D-50: `derivable` cases are derived from the cited package text; `pending-evidence` cases depend on a missing package and are listed, never asserted. A pending case is never counted as proven. "SideEffects=Create" marks the eight create-path tests that `OracleFactAttribute` keeps skipped until `docs/dependency-open-items.md` §4 records the isolation inspection as `Cleared`.

| PR | Rule [package locator] | Legacy counterpart | Reached through | Parity test | Case statuses | Status |
|---|---|---|---|---|---|---|
| PR-01 | Gross = `round_money(price*qty)`; price override null / negative / not allowed (-20780, -20781, -20963) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1053-1080, 1946] | PU10 `SMALL_CALC`, T067 | `BilInvoiceApiGateway.CalculatePreview`, `.CreateFullInvoice` | `PackageParityTests.PR01_GrossAndPriceOverride` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-02 | Line discount R / V amount and percent; negative, over-100 and over-gross checks (-20906 … -20911, -20919 … -20921) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:542-614, 1120-1173] | PU17 `DO_DISC`, T070, T071 | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR02_LineDiscounts` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-03 | Plan discount, non-discountable, combined over gross (-20945, -20922, -20961) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1946-2014] | PU19 `OKA`, PU10 `SMALL_CALC` | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR03_PlanDiscountAndDiscountability` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-04 | Payer share via `bil_class_rule.resolve_share` with this invoice's running patient share [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:2016-2025], including advanced classes and `DIRECT_COMP_SHARE`; the prior-payment part of the cap is OI-23 (D-51) | PU23 `CHK_ADV_CLASS`, PU10, T026, T052, T054, T072, T073 | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR04_PayerShare` | `pending-evidence` (OI-06) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset; pending evidence (OI-06) |
| PR-05 | VAT patient / company, exempt amount (-20912) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1175-1232] | PU10 `SMALL_CALC` | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR05_Vat` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-06 | Final discount from amount or percent × total net; negative / over patient share (-20901, -20902, -20913, -20914) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1234-1262] | T039, T041 | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR06_FinalDiscount` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-07 | Amount due `cash_collected = round_money(pat_pay − finaldisc + vat_total_pat)`; negative → -20916 [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:3058-3062] | PU20 `ROUND_FOR_CASH` | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR07_AmountDue` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-08 | Payment status 'No Amount Due' / 'Unpaid' / 'Partial' / 'Paid' / 'Overpaid'; amount 1 auto; remaining [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:782-943] | PU10 (`amount_1 := net`) | `CalculatePreview` | `PackageParityTests.PR08_PaymentStatus` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-09 | Invoice number via `GET_NEXT_INVOICE_NO`, 20 attempts, else -20909 [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1082-1102] | T011 | `CreateFullInvoice` | `PackageParityTests.PR09_InvoiceNumbering` (SideEffects=Create) | `pending-evidence` (OI-19) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset; pending evidence (OI-19) |
| PR-10 | Open-shift gate `bil_cashier_shift.assert_can_create_invoice`, called unconditionally [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:3203-3207]; switch and admin bypass not reproduced (D-25) | T003, T011 | `CreateFullInvoice` | `PackageParityTests.PR10_OpenShiftGate` (SideEffects=Create) | `pending-evidence` (OI-01) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset; pending evidence (OI-01) |
| PR-11 | `OLD_OR_NEW` from prior invoices [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1104-1118] | T011 | `CreateFullInvoice` | `PackageParityTests.PR11_OldOrNew` (SideEffects=Create) | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-12 | `PFLAG` (hour ≥ 12 → PM), `THE_MONTH`, `THE_YEAR` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:455-467, 2769-2771] | T015, T025, T011 | `CreateFullInvoice` | `PackageParityTests.PR12_PflagMonthYear` (SideEffects=Create) | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-13 | Header validation (-20900, -20923, -20924, -20964, -20915) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:493-540, 3135-3200] | T014 (re-check) | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR13_HeaderValidation` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-14 | Line validation: a line (-20903); service id (-20904) only when the line's offer metadata make the parent exemption false (`offer_type` set and not 0, or `offer_line_role` set and not 'PARENT'), so an ordinary line with both null is not rejected (§10); positive whole quantity (-20905); ceiling `trunc(c_max_invoice_qty)` = 999999 (constant 999999.99), applied only to package parent and component quantities (-20947, -20966) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:171-182, 542-614, 974, 1583, 1698, 2461] | T009, T068 (re-check); the mandatory service is the Form's DR-15 (T066), not this check | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR14_LineValidation` | `derivable`; the -20904 case uses an offer-shaped line (D-126) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-15 | Currency from the price list; mixed currencies rejected (-20917, -20918) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1390-1420] | PU10 (`CURR_CODE`) | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR15_Currency` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-16 | Standard-offer eligibility and conflicts (-20971, -20972) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1867-1884] | T066 standard-offer branch; T085 repeats it on imported request lines | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR16_StandardOffers` | `derivable` (gate and validations); `pending-evidence` (offer applied, OI-07) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-17 | Bundled offer: cash only (-20871), required fields and quantities (-20979), row validity (-20978), component qty = bundle qty × detail qty [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:264-779] | T093 | `BilInvoiceApiGateway.GetBundledOfferLines`; expansion in preview / create | `PackageParityTests.PR17_BundledOffer` | `derivable` (gate and validations); `pending-evidence` (offer applied, OI-07) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-18 | Package expansion and definition token; stale package (-20944, -20946, -20969) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:320-351, 1306-1322] | T089 | `BilInvoiceApiGateway.GetPackageLines` (D-38); preview / create | `PackageParityTests.PR18_PackageExpansion` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-19 | Request import: session-scoped selection, rejected rows skipped, credit rows needing approval skipped in mode 1 and imported in mode 0 (-20764 when raising) [05_Complex/APEX_Reference/backend/BIL_IMPORT.sql:389-565, 494-509] | T085 | `BilImportGateway.ImportRequestLines` (D-12) | `PackageParityTests.PR19_RequestImport` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-20 | Request-line availability at create (-20931) and link to `PAT_SERV_REQ` (-20930) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:745-772, 2952-2965] | T061, T055 | `CreateFullInvoice` | `PackageParityTests.PR20_RequestLineAvailability` (SideEffects=Create) | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-21 | Visit consultation / review line from `DOCTOR_CONSULTATION` on the patient-context price list (-20752 … -20755) [05_Complex/APEX_Reference/backend/BIL_IMPORT.sql:120-130, 1213-1230]; the service '2000' branch is DR-25 | T029, T030 | `BilImportGateway.GetVisitLine` | `PackageParityTests.PR21_VisitLine` | `derivable` | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-22 | Idempotent create by request id (-20847, -20848, -20849; a replay returns the existing invoice) [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:1303-1373] | — (replaces the Form's single-session commit) | `CreateFullInvoice` (D-54) | `PackageParityTests.PR22_IdempotentCreate` (SideEffects=Create) | `derivable`; no -20847 case, the gateway refuses such ids first (D-127) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |
| PR-23 | Posting stages at create (payment, queue, stock) [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:1385-1424]; print and SMS stages called with `'N'` (D-13, D-28) | T016, T061 | `CreateFullInvoice` | `PackageParityTests.PR23_PostingStages` (SideEffects=Create) | `pending-evidence` (OI-08 … OI-10) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset; pending evidence (OI-08) |
| PR-24 | Payer context (price list, VAT flags, cash flag) from `bil_patient_context.get_context` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:3004-3018]; the engine's default pay type is not reached, because DR-24 always sends `paytype` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:478-491] | T023, T027, PU16 `MAKE_CASH` | `CalculatePreview`, `CreateFullInvoice` | `PackageParityTests.PR24_PayerContext` | `pending-evidence` (OI-03) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset; pending evidence (OI-03) |
| PR-25 | Offer changed after calculation (-20970) [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1306-1322] | T066 standard-offer branch; the Form reads the offer at service validation and never re-checks it at commit | `CreateFullInvoice` | `PackageParityTests.PR25_OfferStale` (SideEffects=Create) | `derivable`; the orphan-metadata case carries no offer instance id (D-128) | UNVERIFIED — skipped, ORACLE_TEST_CONNECTION unset |

`OracleBindingSmokeTests` (`Category=OracleIntegration`) exercises the binding mechanism of the six PL/SQL blocks; it is not parity and is not counted in the PR figure.

## §7 APEX reconciliation

The APEX Page 48 export and its JavaScript are references only. Where they disagree with the Form, the Form governs; where only APEX speaks, nothing is built and an open item is registered.

**Differences where the legacy behaviour is adopted**

| APEX behaviour [locator] | Legacy behaviour adopted |
|---|---|
| `val-payment-type-required` blocks: 'Select Payment Method 1 when Amount 1 is greater than zero.' [05_Complex/APEX_Reference/hmisfox_page_48.apx:6362] | T014 warns 'Payment type is empty' and defaults `SUB_PAYTYPE := 1` (DR-22) |
| `val-doctor-required` 'Doctor must have some value.' [05_Complex/APEX_Reference/hmisfox_page_48.apx:6260] | 'Doctor No is required ' (DR-01) |
| `val-pay-type-required` 'Payer Type must have a value.' [05_Complex/APEX_Reference/hmisfox_page_48.apx:6343] | Pay type derived from company and patient context (DR-24, PR-24); credit without a company blocked by DR-01 |
| Incomplete line 'Select a service or remove the incomplete invoice line.' [05_Complex/APEX_Reference/javascript/page48-invoice-preview.js:2530] | 'You Must Select Value' (DR-15) |
| 'Quantity must be a positive whole number.' [05_Complex/APEX_Reference/javascript/page48-invoice-preview.js:2559] | 'Qty should be >=1' (DR-12); the package still rejects fractions (-20905, UNVERIFIED) |
| Request import raises on blocked rows: Page 48 calls `BUILD_IMPORT_PREVIEW_COLLECTION` [05_Complex/APEX_Reference/hmisfox_page_48.apx:6977], which passes `p_raise_on_blocked => 'Y'` [05_Complex/APEX_Reference/backend/BIL_INVOICE_API.sql:1127-1136] | T085 skips rejected and need-approval rows with a per-service notice: `p_raise_on_blocked => 'N'`, approval mode 0 when `X422_APPROV_CHECK=2` (D-12, D-26) |
| SMS sent only on "create and print", to `PATIENT.PHONE_H` [05_Complex/APEX_Reference/hmisfox_page_48.apx:9090, 10129-10134] | T016 sends on every cash (company '0') save a link to `inv_small_cash.jsp` [05_Complex/Inv_Small_Cash.xml:368]; not reproducible (OI-12, OI-45), so no SMS is sent and the create response lists both (D-13, D-28) |
| Request rows from the session-scoped `BIL_REQUEST_INV_SELECTION`, written by `BIL_IMPORT.SET_REQUEST_LINE_SELECTION` [05_Complex/APEX_Reference/backend/BIL_IMPORT.sql:94-103] (UNVERIFIED) and checked by `validate-request-invoice-context` [05_Complex/APEX_Reference/hmisfox_page_48.apx:10183-10384]; nothing in the page writes the selection (OI-39) | Rows flagged `SELECT_TO_INV=1` on the visit are imported (D-12) |
| Line removal takes a whole package or bundled-offer occurrence and refuses mixed selections ('Remove bundled offer lines separately from normal or package lines.') [05_Complex/APEX_Reference/javascript/page48-invoice-services-init.js:947-1319] | T058 deletes the one current line and recalculates [05_Complex/Inv_Small_Cash.xml:565]; the package re-validates the rest (for example -20952, UNVERIFIED) |

**APEX checks that duplicate package rules** (the package governs; nothing added; the duplication is UNVERIFIED): the discount-consistency validations [05_Complex/APEX_Reference/hmisfox_page_48.apx:5933-6004, 6044-6070, 6097-6147] duplicate engine -20906 … -20921; `doctor-belongs-to-specialty` [05_Complex/APEX_Reference/hmisfox_page_48.apx:6005] duplicates -20924; `val-final-disc-non-negative` [05_Complex/APEX_Reference/hmisfox_page_48.apx:6279] duplicates -20902.

**APEX-only behaviours** (the Form is silent; not implemented):

- OI-34 'Payment Method 1 and Payment Method 2 cannot be the same.' [05_Complex/APEX_Reference/hmisfox_page_48.apx:6071]
- OI-35 'Amount 1 cannot be negative.' and the Amount 2 equivalent [05_Complex/APEX_Reference/hmisfox_page_48.apx:6148, 6177]
- OI-36 'Select Payment Method 2 when Amount 2 is greater than zero.' / 'Enter Amount 2 or clear Payment Method 2.' [05_Complex/APEX_Reference/hmisfox_page_48.apx:6206, 6308]
- OI-37 'Specialty must have some value.' [05_Complex/APEX_Reference/hmisfox_page_48.apx:6241]
- OI-38 JavaScript safeguards: stale-preview rejection [05_Complex/APEX_Reference/javascript/page48-invoice-preview.js:4555-4799], recalculation debounce [05_Complex/APEX_Reference/javascript/page48-invoice-preview.js:4856-4902], submit lock [05_Complex/APEX_Reference/javascript/page48-function-and-global-variable-declaration.js:191, 338], reserved print window [05_Complex/APEX_Reference/javascript/page48-function-and-global-variable-declaration.js:238]
- OI-39 Draft persistence in `BIL_INV_DRAFT_LINES` [05_Complex/APEX_Reference/hmisfox_page_48.apx:8699-8866] and the request-selection UI
- OI-57 Whole-occurrence package / bundle removal and refusal of mixed or malformed removal selections [05_Complex/APEX_Reference/javascript/page48-invoice-services-init.js:947-1319]
- OI-58 Per-row editability locks: manual discount only where `ALLOW_MANUAL_DISCOUNT`, price override only where `ALLOW_PRICE_OVERRIDE`, package and offer component rows read-only [05_Complex/APEX_Reference/javascript/page48-invoice-preview.js:1154-1406]; the package's own checks (-20963, the discount errors) still apply (UNVERIFIED)

**APEX is silent** on these Form rules the target keeps: the open-shift gate (PR-10), the `MAX_DISC` limit (DR-06), the refund (DR-08), clinic sex and age (DR-04), revisit limits (blocked, OI-32), the GP-at-ER rule (DR-05) and the invoice-admin bypass (not reproduced, OI-01).

## §8 Oracle error register

- **Call count:** in each package source, every case-insensitive `raise_application_error\s*\(` outside `--` and `/* … */` comments: BIL_INVOICE_ENGINE 85, BIL_IMPORT 54, BIL_INVOICE_API 17, total 156.
- **Extraction:** comments are stripped outside single-quoted literals; each call is scanned from its opening parenthesis with a quote-aware scanner (`''` is an escaped quote) that tracks nested parentheses; top-level commas split the arguments. Argument 1 is the number, argument 2 the message template with every whitespace run collapsed to one space.
- **Numbers:** 119 distinct. One first argument is an identifier: `c_request_unavailable_error`, declared at [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:176] as -20931 and raised at line 619. Zero sites are unparsed.
- **Columns:** `Line` is the 1-based line of the `raise_application_error` token. `Catalogue prefix` is the `OracleErrorCatalog` prefix for a catalogued site (the longest, when several match), else the template's leading literal (`—` when it starts with an expression). The prefix is the exact text, except that its trailing blank is shown as `␠` (U+2420 SYMBOL FOR SPACE), which stands for exactly one space character and occurs in no package message. `Catalogued` is `Yes` when `OracleErrorCatalog.Rows` holds the package and number with a prefix that starts the leading literal. `Target handling` is the catalogue kind, the mapped field, a catalogued form-level 422, or `Generic 422`.

| Package | Line | Number | Message template | Catalogue prefix | Catalogued | Target handling |
|---|---|---|---|---|---|---|
| BIL_INVOICE_ENGINE | 320 | -20945 | 'Invoice create failed: package definition is too large to validate.' | Invoice create failed: package definition is too large to validate. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 337 | -20944 | 'Invoice create failed: package ' \|\| p_package_serviceid \|\| ' has no configured components.' | Invoice create failed: package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 351 | -20946 | 'Invoice create failed: package ' \|\| p_package_serviceid \|\| ' is not active in list ' \|\| p_list_id \|\| '.' | Invoice create failed: package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 500 | -20900 | 'Invoice create failed: patient number is required.' | Invoice create failed: patient number is required. | Yes | Field PATIENTNO |
| BIL_INVOICE_ENGINE | 507 | -20901 | 'Invoice create failed: final discount percent must be between 0 and 100.' | Invoice create failed: final discount percent must be between 0 and 100. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 513 | -20902 | 'Invoice create failed: final discount amount cannot be negative.' | Invoice create failed: final discount amount cannot be negative. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 520 | -20923 | 'Invoice create failed: clinic is required when doctor is supplied.' | Invoice create failed: clinic is required when doctor is supplied. | Yes | Field CLINICID |
| BIL_INVOICE_ENGINE | 534 | -20924 | 'Invoice create failed: selected doctor does not belong to the selected clinic.' | Invoice create failed: selected doctor does not belong to the selected clinic. | Yes | Field DOCIDX |
| BIL_INVOICE_ENGINE | 550 | -20903 | 'Invoice create failed: at least one service line is required.' | Invoice create failed: at least one service line is required. | Yes | Form-level 422, legacy text 'Invoice without Details' |
| BIL_INVOICE_ENGINE | 560 | -20904 | 'Invoice create failed: service ID is required on line ' \|\| l_idx \|\| '.' | Invoice create failed: service ID is required on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 569 | -20905 | 'Invoice create failed: quantity must be a positive whole number on line ' \|\| l_idx \|\| '.' | Invoice create failed: quantity must be a positive whole number on line␠ | Yes | Field QTY |
| BIL_INVOICE_ENGINE | 575 | -20906 | 'Invoice create failed: discount percent cannot be negative on line ' \|\| l_idx \|\| '.' | Invoice create failed: discount percent cannot be negative on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 581 | -20907 | 'Invoice create failed: discount amount cannot be negative on line ' \|\| l_idx \|\| '.' | Invoice create failed: discount amount cannot be negative on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 588 | -20908 | 'Invoice create failed: discount type must be N, R or V on line ' \|\| l_idx \|\| '.' | Invoice create failed: discount type must be N, R or V on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 595 | -20919 | 'Invoice create failed: no discount line must have zero discount percent and amount on line ' \|\| l_idx \|\| '.' | Invoice create failed: no discount line must have zero discount percent and amount on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 601 | -20920 | 'Invoice create failed: percent discount line cannot have discount amount on line ' \|\| l_idx \|\| '.' | Invoice create failed: percent discount line cannot have discount amount on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 607 | -20921 | 'Invoice create failed: amount discount line cannot have discount percent on line ' \|\| l_idx \|\| '.' | Invoice create failed: amount discount line cannot have discount percent on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 619 | -20931 | 'One or more requested services were already invoiced or are no longer available.' | One or more requested services were already invoiced or are no longer available. | Yes | RequestLinesStale |
| BIL_INVOICE_ENGINE | 887 | -20965 | 'Invoice create failed: configured cash company could not be resolved.' | Invoice create failed: configured cash company could not be resolved. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 965 | -20967 | 'Invoice create failed: package definition contains an invalid component.' | Invoice create failed: package definition contains an invalid component. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 976 | -20966 | 'Invoice create failed: multiplied package component quantity must be a positive whole number within the invoice quantity limit.' | Invoice create failed: multiplied package component quantity must be a positive whole number within the invoice quantity limit. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1006 | -20968 | 'Invoice create failed: requested package has no components.' | Invoice create failed: requested package has no components. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1065 | -20780 | 'Invoice line ' \|\| p_line_no \|\| ' uses price override but PRICE_OVERRIDE is null.' | Invoice line␠ | Yes | Field PRICE |
| BIL_INVOICE_ENGINE | 1072 | -20781 | 'Invoice line ' \|\| p_line_no \|\| ' price override cannot be negative.' | Invoice line␠ | Yes | Field PRICE |
| BIL_INVOICE_ENGINE | 1098 | -20909 | 'Invoice create failed: could not generate a unique invoice number after 20 attempts.' | Invoice create failed: could not generate a unique invoice number after 20 attempts. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1136 | -20910 | 'Invoice create failed: line discount percent cannot exceed 100.' | Invoice create failed: line discount percent cannot exceed 100. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1145 | -20911 | 'Invoice create failed: line discount amount cannot exceed line gross amount.' | Invoice create failed: line discount amount cannot exceed line gross amount. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1192 | -20912 | 'Invoice create failed: VAT rate must be between 0 and 100 for service ' \|\| p_service_context.serviceid | Invoice create failed: VAT rate must be between 0 and 100 for service␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1250 | -20913 | 'Invoice create failed: final discount cannot be negative.' | Invoice create failed: final discount cannot be negative. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1256 | -20914 | 'Invoice create failed: final discount cannot exceed patient share.' | Invoice create failed: final discount cannot exceed patient share. | Yes | Field FINALDISC, legacy text 'discount is greater than cash payed amount' |
| BIL_INVOICE_ENGINE | 1308 | -20969 | 'The package definition changed after the invoice was calculated. ' \|\| 'Refresh the invoice and review the package lines.' | The package definition changed after the invoice was calculated.␠ | Yes | DefinitionStale |
| BIL_INVOICE_ENGINE | 1317 | -20970 | 'The offer changed after the invoice was calculated. ' \|\| 'Refresh the invoice and review the updated pricing.' | The offer changed after the invoice was calculated.␠ | Yes | DefinitionStale |
| BIL_INVOICE_ENGINE | 1399 | -20917 | 'Invoice create failed: currency is missing from price list/service for line ' \|\| p_line_no \|\| ', service ' \|\| p_serviceid \|\| '.' | Invoice create failed: currency is missing from price list/service for line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1411 | -20918 | 'Invoice create failed: mixed currencies are not allowed. Expected ' \|\| io_result.curr_code \|\| ' but line ' \|\| p_line_no \|\| ' uses ' \|\| l_curr_code \|\| '.' | Invoice create failed: mixed currencies are not allowed. Expected␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1445 | -20940 | 'Invoice create failed: package line ' \|\| p_line_no \|\| ' cannot use a price override.' | Invoice create failed: package line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1460 | -20941 | 'Invoice create failed: package line ' \|\| p_line_no \|\| ' cannot use a manual discount.' | Invoice create failed: package line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1486 | -20942 | 'Invoice create failed: package ' \|\| p_package_serviceid \|\| ' has a blank component.' | Invoice create failed: package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1497 | -20943 | 'Invoice create failed: package ' \|\| p_package_serviceid \|\| ' has an invalid component quantity.' | Invoice create failed: package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1510 | -20944 | 'Invoice create failed: package ' \|\| p_package_serviceid \|\| ' has no configured components.' | Invoice create failed: package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1554 | -20948 | 'Invoice create failed: package occurrence contains an invalid line role.' | Invoice create failed: package occurrence contains an invalid line role. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1564 | -20948 | 'Invoice create failed: package occurrence must contain exactly one parent.' | Invoice create failed: package occurrence must contain exactly one parent. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1574 | -20949 | 'Invoice create failed: package parent service metadata was changed.' | Invoice create failed: package parent service metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1585 | -20947 | 'Invoice create failed: package parent quantity must be a positive whole number not greater than ' \|\| trunc(c_max_invoice_qty) \|\| ' for package ' \|\| l_package_serviceid \|\| '.' | Invoice create failed: package parent quantity must be a positive whole number not greater than␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1604 | -20946 | 'Invoice create failed: package ' \|\| l_package_serviceid \|\| ' is not active in the patient price list.' | Invoice create failed: package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1613 | -20946 | 'Invoice create failed: invalid package pricing method for package ' \|\| l_package_serviceid \|\| '.' | Invoice create failed: invalid package pricing method for package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1632 | -20951 | 'Invoice create failed: package components are missing or extra.' | Invoice create failed: package components are missing or extra. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1642 | -20955 | 'Invoice create failed: package line parent metadata was changed.' | Invoice create failed: package line parent metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1650 | -20962 | 'Invoice create failed: package pricing method metadata was changed.' | Invoice create failed: package pricing method metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1688 | -20952 | 'Invoice create failed: package component service was changed, duplicated, or reordered.' | Invoice create failed: package component service was changed, duplicated, or reordered. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1700 | -20966 | 'Invoice create failed: multiplied package component quantity must be a positive whole number within the invoice quantity limit.' | Invoice create failed: multiplied package component quantity must be a positive whole number within the invoice quantity limit. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1905 | -20971 | 'A manual price cannot be combined with a Standard Offer.' | A manual price cannot be combined with a Standard Offer. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1908 | -20972 | 'A manual line discount cannot be combined with a Standard Offer.' | A manual line discount cannot be combined with a Standard Offer. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1925 | -20963 | 'Invoice create failed: service ' \|\| p_line.serviceid \|\| ' does not allow a price override on line ' \|\| p_line_no \|\| '.' | Invoice create failed: service␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1956 | -20945 | 'Invoice create failed: plan discount must be between 0 and 100 for service ' \|\| p_line.serviceid \|\| '.' | Invoice create failed: plan discount must be between 0 and 100 for service␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1966 | -20922 | 'Invoice create failed: service ' \|\| p_line.serviceid \|\| ' is non-discountable on line ' \|\| p_line_no \|\| '.' | Invoice create failed: service␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 1992 | -20961 | 'Invoice create failed: combined plan and manual discount cannot exceed gross amount on line ' \|\| p_line_no \|\| '.' | Invoice create failed: combined plan and manual discount cannot exceed gross amount on line␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2184 | -20973 | 'Bundled Offer instance identity is required.' | Bundled Offer instance identity is required. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2199 | -20974 | 'Bundled Offer line role is invalid.' | Bundled Offer line role is invalid. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2206 | -20975 | 'Bundled Offer occurrence must contain exactly one parent.' | Bundled Offer occurrence must contain exactly one parent. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2222 | -20976 | 'Bundled Offer parent was changed.' | Bundled Offer parent was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2366 | -20966 | 'Invoice create failed: bundled offer component quantity must be a positive whole number.' | Invoice create failed: bundled offer component quantity must be a positive whole number. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2388 | -20977 | 'Bundled Offer components cannot be service packages.' | Bundled Offer components cannot be service packages. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2451 | -20946 | 'Invoice create failed: invalid package pricing method for package ' \|\| p_lines(l_idx).serviceid \|\| '.' | Invoice create failed: invalid package pricing method for package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2463 | -20947 | 'Invoice create failed: package parent quantity must be a positive whole number not greater than ' \|\| trunc(c_max_invoice_qty) \|\| ' for package ' \|\| p_lines(l_idx).serviceid \|\| '.' | Invoice create failed: package parent quantity must be a positive whole number not greater than␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2474 | -20948 | 'Invoice create failed: package parent role was changed.' | Invoice create failed: package parent role was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2478 | -20949 | 'Invoice create failed: package parent service metadata was changed.' | Invoice create failed: package parent service metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2482 | -20962 | 'Invoice create failed: package pricing method metadata was changed.' | Invoice create failed: package pricing method metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2496 | -20949 | 'Invoice create failed: package instance identity is required.' | Invoice create failed: package instance identity is required. | Yes | UnexpandedPackageParent |
| BIL_INVOICE_ENGINE | 2505 | -20950 | 'Invoice create failed: fixed-price package ' \|\| p_lines(l_idx).serviceid \|\| ' has no valid package plan price.' | Invoice create failed: fixed-price package␠ | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2549 | -20951 | 'Invoice create failed: package component line is missing.' | Invoice create failed: package component line is missing. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2552 | -20952 | 'Invoice create failed: package component service was changed or reordered.' | Invoice create failed: package component service was changed or reordered. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2555 | -20966 | 'Invoice create failed: authoritative package component quantity is missing.' | Invoice create failed: authoritative package component quantity is missing. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2565 | -20954 | 'Invoice create failed: package component role was changed.' | Invoice create failed: package component role was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2569 | -20955 | 'Invoice create failed: package component parent metadata was changed.' | Invoice create failed: package component parent metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2573 | -20956 | 'Invoice create failed: package component instance metadata was changed.' | Invoice create failed: package component instance metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2577 | -20957 | 'Invoice create failed: package component order metadata was changed.' | Invoice create failed: package component order metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2581 | -20962 | 'Invoice create failed: package pricing method metadata was changed.' | Invoice create failed: package pricing method metadata was changed. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2596 | -20958 | 'Invoice create failed: nested packages are not supported.' | Invoice create failed: nested packages are not supported. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2650 | -20959 | 'Invoice create failed: package component was submitted without its package parent.' | Invoice create failed: package component was submitted without its package parent. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2817 | -20960 | 'Invoice create failed: component parent line metadata is invalid.' | Invoice create failed: component parent line metadata is invalid. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 2958 | -20930 | 'Invoice create failed: request line ' \|\| p_calc_lines(l_idx).pat_serv_req_row_id \|\| ' was already invoiced by another session.' | Invoice create failed: request line␠ | Yes | RequestLinesStale |
| BIL_INVOICE_ENGINE | 3062 | -20916 | 'Invoice preview failed: cash collected cannot be negative.' | Invoice preview failed: cash collected cannot be negative. | Yes | Form-level 422 |
| BIL_INVOICE_ENGINE | 3156 | -20964 | 'Invoice create failed: creation date/time is required.' | Invoice create failed: creation date/time is required. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 3170 | -20915 | 'Invoice create failed: user number is required.' | Invoice create failed: user number is required. | No | Generic 422 |
| BIL_INVOICE_ENGINE | 3229 | -20916 | 'Invoice create failed: cash collected cannot be negative.' | Invoice create failed: cash collected cannot be negative. | Yes | Form-level 422 |
| BIL_IMPORT | 188 | -20759 | p_name \|\| ' must be Y or N.' | — | No | Generic 422 |
| BIL_IMPORT | 243 | -20760 | 'Import failed: pay type must be 1 Cash or 2 Credit.' | Import failed: pay type must be 1 Cash or 2 Credit. | No | Generic 422 |
| BIL_IMPORT | 257 | -20761 | 'Import failed: service ID is required. Context: ' \|\| p_context | Import failed: service ID is required. Context:␠ | No | Generic 422 |
| BIL_IMPORT | 271 | -20690 | 'Request package expansion failed: request row ID is required.' | Request package expansion failed: request row ID is required. | No | Generic 422 |
| BIL_IMPORT | 279 | -20691 | 'Request package expansion failed: package instance ID exceeds 64 characters.' | Request package expansion failed: package instance ID exceeds 64 characters. | No | Generic 422 |
| BIL_IMPORT | 307 | -20692 | 'Request price validation failed: invoice date/time is required.' | Request price validation failed: invoice date/time is required. | No | Generic 422 |
| BIL_IMPORT | 313 | -20693 | 'Request price validation failed: quantity must be greater than zero.' | Request price validation failed: quantity must be greater than zero. | No | Generic 422 |
| BIL_IMPORT | 319 | -20694 | 'Request price validation failed: configured cash company could not be resolved.' | Request price validation failed: configured cash company could not be resolved. | No | Generic 422 |
| BIL_IMPORT | 329 | -20695 | 'Request price validation failed: payer type and company context are inconsistent.' | Request price validation failed: payer type and company context are inconsistent. | No | Generic 422 |
| BIL_IMPORT | 350 | -20696 | 'Request price validation failed: a FREE package cannot have a nonzero request price.' | Request price validation failed: a FREE package cannot have a nonzero request price. | No | Generic 422 |
| BIL_IMPORT | 355 | -20697 | 'Request price validation failed: a COMPONENT_PRICE package parent cannot have a nonzero request price.' | Request price validation failed: a COMPONENT_PRICE package parent cannot have a nonzero request price. | No | Generic 422 |
| BIL_IMPORT | 366 | -20698 | 'Request price validation failed: service ' \|\| p_service_context.serviceid \|\| ' has a fixed price and the changed request price cannot be honored.' | Request price validation failed: service␠ | No | Generic 422 |
| BIL_IMPORT | 375 | -20699 | 'Request price validation failed: changed request prices are allowed only for the configured cash company.' | Request price validation failed: changed request prices are allowed only for the configured cash company. | No | Generic 422 |
| BIL_IMPORT | 409 | -20762 | 'Request import failed: patient number is required.' | Request import failed: patient number is required. | No | Generic 422 |
| BIL_IMPORT | 415 | -20763 | 'Request import failed: visit unique is required.' | Request import failed: visit unique is required. | No | Generic 422 |
| BIL_IMPORT | 421 | -20778 | 'Request import failed: application ID is required.' | Request import failed: application ID is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 427 | -20779 | 'Request import failed: application session is required.' | Request import failed: application session is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 433 | -20782 | 'Request import failed: application user is required.' | Request import failed: application user is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 440 | -20758 | 'Request import failed: approval check mode must be 0 or 1.' | Request import failed: approval check mode must be 0 or 1. | No | Generic 422 |
| BIL_IMPORT | 501 | -20764 | 'Request import failed: service ' \|\| r.serviceid \|\| ' needs approval before import.' | Request import failed: service␠ | No | Generic 422 |
| BIL_IMPORT | 511 | -20770 | 'Request import failed: selected request line ' \|\| r.pat_serv_req_row_id \|\| ' has no service ID.' | Request import failed: selected request line␠ | No | Generic 422 |
| BIL_IMPORT | 519 | -20771 | 'Request import failed: selected request line ' \|\| r.pat_serv_req_row_id \|\| ' has invalid quantity.' | Request import failed: selected request line␠ | Yes | Form-level 422 |
| BIL_IMPORT | 594 | -20692 | 'Request package expansion failed: invoice date/time is required.' | Request package expansion failed: invoice date/time is required. | No | Generic 422 |
| BIL_IMPORT | 628 | -20694 | 'Request package expansion failed: configured cash company could not be resolved.' | Request package expansion failed: configured cash company could not be resolved. | No | Generic 422 |
| BIL_IMPORT | 699 | -20773 | 'Request package expansion failed: invalid component definition for package ' \|\| l_line.serviceid \|\| '.' | Request package expansion failed: invalid component definition for package␠ | Yes | Form-level 422 |
| BIL_IMPORT | 709 | -20771 | 'Request package expansion failed: multiplied quantity exceeds the supported two-decimal quantity precision for component ' \|\| r.sub_serviceid \|\| '.' | Request package expansion failed: multiplied quantity exceeds the supported two-decimal quantity precision for component␠ | Yes | Form-level 422 |
| BIL_IMPORT | 748 | -20774 | 'Request package expansion failed: package ' \|\| l_line.serviceid \|\| ' has no components.' | Request package expansion failed: package␠ | Yes | Form-level 422 |
| BIL_IMPORT | 783 | -20772 | 'Request selection failed: patient number is required.' | Request selection failed: patient number is required. | Yes | Form-level 422 |
| BIL_IMPORT | 790 | -20773 | 'Request selection failed: visit unique is required.' | Request selection failed: visit unique is required. | Yes | Form-level 422 |
| BIL_IMPORT | 797 | -20774 | 'Request selection failed: request line row ID is required.' | Request selection failed: request line row ID is required. | Yes | Form-level 422 |
| BIL_IMPORT | 806 | -20778 | 'Request selection failed: application ID is required.' | Request selection failed: application ID is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 813 | -20779 | 'Request selection failed: application session is required.' | Request selection failed: application session is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 820 | -20782 | 'Request selection failed: application user is required.' | Request selection failed: application user is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 827 | -20775 | 'Request selection failed: select value must be 0 or 1.' | Request selection failed: select value must be 0 or 1. | No | Generic 422 |
| BIL_IMPORT | 846 | -20776 | 'Request selection failed: the request line is no longer available.' | Request selection failed: the request line is no longer available. | No | Generic 422 |
| BIL_IMPORT | 911 | -20777 | 'Request selection clear failed: patient number is required.' | Request selection clear failed: patient number is required. | No | Generic 422 |
| BIL_IMPORT | 918 | -20778 | 'Request selection clear failed: application ID is required.' | Request selection clear failed: application ID is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 925 | -20779 | 'Request selection clear failed: application session is required.' | Request selection clear failed: application session is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 932 | -20782 | 'Request selection clear failed: application user is required.' | Request selection clear failed: application user is required. | Yes | OperatorContextMissing |
| BIL_IMPORT | 964 | -20766 | 'Package import failed: list ID is required.' | Package import failed: list ID is required. | No | Generic 422 |
| BIL_IMPORT | 978 | -20767 | 'Package import failed: service ' \|\| p_package_serviceid \|\| ' is not a package in list ' \|\| p_list_id \|\| '.' | Package import failed: service␠ | No | Generic 422 |
| BIL_IMPORT | 988 | -20768 | 'Package import failed: package pricing method is invalid for service ' \|\| p_package_serviceid \|\| '.' | Package import failed: package pricing method is invalid for service␠ | No | Generic 422 |
| BIL_IMPORT | 1062 | -20772 | 'Package import failed: package service ' \|\| p_package_serviceid \|\| ' has a blank sub-service in PACKAGE_DTL.' | Package import failed: package service␠ | Yes | Form-level 422 |
| BIL_IMPORT | 1070 | -20773 | 'Package import failed: package service ' \|\| p_package_serviceid \|\| ' has invalid quantity for sub-service ' \|\| r.sub_serviceid \|\| '.' | Package import failed: package service␠ | Yes | Form-level 422 |
| BIL_IMPORT | 1154 | -20750 | 'Visit import failed: patient number is required.' | Visit import failed: patient number is required. | No | Generic 422 |
| BIL_IMPORT | 1160 | -20751 | 'Visit import failed: doctor is required.' | Visit import failed: doctor is required. | No | Generic 422 |
| BIL_IMPORT | 1167 | -20756 | 'Visit import failed: invoice date/time is required.' | Visit import failed: invoice date/time is required. | No | Generic 422 |
| BIL_IMPORT | 1178 | -20757 | 'Visit import failed: clinic is required.' | Visit import failed: clinic is required. | No | Generic 422 |
| BIL_IMPORT | 1185 | -20758 | 'Visit import failed: info center is required.' | Visit import failed: info center is required. | No | Generic 422 |
| BIL_IMPORT | 1199 | -20759 | 'Visit import failed: selected doctor is not active in the selected clinic.' | Visit import failed: selected doctor is not active in the selected clinic. | No | Generic 422 |
| BIL_IMPORT | 1216 | -20752 | 'Visit import failed: new visit type must be CONSULTATION or REVIEW.' | Visit import failed: new visit type must be CONSULTATION or REVIEW. | No | Generic 422 |
| BIL_IMPORT | 1232 | -20753 | 'Visit import failed: no consultation/review setup was found for this doctor and patient price list.' | Visit import failed: no consultation/review setup was found for this doctor and patient price list. | No | Generic 422 |
| BIL_IMPORT | 1237 | -20754 | 'Visit import failed: consultation/review setup is duplicated for this doctor and patient price list.' | Visit import failed: consultation/review setup is duplicated for this doctor and patient price list. | No | Generic 422 |
| BIL_IMPORT | 1243 | -20755 | case when l_new_visit_type = 'CONSULTATION' then 'Visit import failed: no consultation service is configured for this doctor and patient price list.' else 'Visit import failed: no review service is configured for this doctor and patient price list.' end | — | No | Generic 422 |
| BIL_INVOICE_API | 296 | -20979 | 'Patient, payer type, invoice date, info center and Bundled Offer are required.' | Patient, payer type, invoice date, info center and Bundled Offer are required. | No | Generic 422 |
| BIL_INVOICE_API | 306 | -20979 | 'Bundled Offer quantity must be a positive whole number.' | Bundled Offer quantity must be a positive whole number. | No | Generic 422 |
| BIL_INVOICE_API | 313 | -20871 | 'Bundled Offers are available only for Cash invoices.' | Bundled Offers are available only for Cash invoices. | Yes | Field OFERID |
| BIL_INVOICE_API | 442 | -20978 | 'Bundled Offer invoice rows are invalid. ' \|\| p_message | Bundled Offer invoice rows are invalid.␠ | No | Generic 422 |
| BIL_INVOICE_API | 1008 | -20896 | 'Imported service quantity must be a positive whole number.' | Imported service quantity must be a positive whole number. | No | Generic 422 |
| BIL_INVOICE_API | 1053 | -20890 | 'Import preview collection name is required.' | Import preview collection name is required. | No | Generic 422 |
| BIL_INVOICE_API | 1057 | -20891 | 'Invalid import preview source mode.' | Invalid import preview source mode. | No | Generic 422 |
| BIL_INVOICE_API | 1061 | -20892 | 'Import preview patient number is required.' | Import preview patient number is required. | No | Generic 422 |
| BIL_INVOICE_API | 1065 | -20893 | 'Import preview pay type must be Cash or Credit.' | Import preview pay type must be Cash or Credit. | No | Generic 422 |
| BIL_INVOICE_API | 1086 | -20894 | 'The new-visit service changed for the selected payer context. Refresh the visit and try again.' | The new-visit service changed for the selected payer context. Refresh the visit and try again. | No | Generic 422 |
| BIL_INVOICE_API | 1164 | -20898 | 'Import preview failed: engine line mapping is incomplete.' | Import preview failed: engine line mapping is incomplete. | No | Generic 422 |
| BIL_INVOICE_API | 1180 | -20897 | 'No invoice lines were found for this source invoice.' | No invoice lines were found for this source invoice. | No | Generic 422 |
| BIL_INVOICE_API | 1289 | -20848 | 'Invoice request ' \|\| l_request_id \|\| ' refers to unavailable invoice ' \|\| p_inv_no \|\| '.' | Invoice request␠ | Yes | IdempotencyConflict |
| BIL_INVOICE_API | 1307 | -20847 | 'Invoice create request ID must contain between 1 and 64 bytes.' | Invoice create request ID must contain between 1 and 64 bytes. | No | Generic 422 |
| BIL_INVOICE_API | 1335 | -20848 | 'Invoice request ' \|\| l_request_id \|\| ' exists but has no completed invoice.' | Invoice request␠ | Yes | IdempotencyConflict |
| BIL_INVOICE_API | 1345 | -20849 | 'Invoice request ' \|\| l_request_id \|\| ' already belongs to invoice ' \|\| l_existing_inv_no \|\| ' for another patient.' | Invoice request␠ | Yes | IdempotencyConflict |
| BIL_INVOICE_API | 1467 | -20848 | 'Invoice request ' \|\| l_request_id \|\| ' could not be completed for invoice ' \|\| l_inv_no \|\| '.' | Invoice request␠ | Yes | IdempotencyConflict |

Reuse flags:

- **Within a package** (numbers raised at more than one site): `BIL_IMPORT` -20692, -20694, -20758, -20759, -20771, -20772, -20773 (3 sites), -20774, -20778 (3), -20779 (3), -20782 (3); `BIL_INVOICE_API` -20848 (3), -20979; `BIL_INVOICE_ENGINE` -20916, -20944, -20945, -20946 (4), -20947, -20948 (3), -20949 (3), -20951, -20952, -20955, -20962 (3), -20966 (4). Unmarked numbers occur at two sites.
- **Across packages:** no number is shared between two of the three packages. The engine's -20780 / -20781 sit inside `BIL_IMPORT`'s -207xx band, and the missing shared packages may reuse any number, so every match is on package, number and message prefix (D-24).
- Every row with `Catalogued` = `No` is handled by the generic rule: HTTP 422 `oracle-business-error` carrying the number, the package and the text after `ORA-2nnnn: `, or the fixed text 'The Oracle error text could not be read.' when the message lacks its own prefix (D-117).

## §9 Bidirectional traceability matrix

The matrix lives only in this section (D-20). Its reverse half is generated from the assemblies and Web sources, not hand-listed (D-56). Schema objects appear as `OI-xx.yy` items of the register in `docs/dependency-open-items.md` (D-57).

**Key conventions.**

- **Forward key** (§9.1): `kind|owner|name|event|line`, written with each `|` escaped as `\|`.
  - `kind` is the element tag of `05_Complex/Inv_Small_Cash.xml`, one of Block (5), Canvas (2), Window (1), Relation (2), Report (1), Alert (5), Trigger (95), ProgramUnit (30), LOV (15), RecordGroup (16), ModuleParameter (32), VisualAttribute (7), Item (202): 413 rows. No other element kind is keyed.
  - `owner` is the `Name` of the nearest enclosing `Block`, else `FORM`.
  - `name` is the element's `Name`; for a `Trigger`, the `Name` of the nearest enclosing `Item`, else of the enclosing `Block`, else `FORM`.
  - `event` is a trigger's `Name` attribute (its event), otherwise `-`.
  - `line` is the 1-based line of the element's start tag.
- **Legacy id:** `T001` … `T095` and `PU01` … `PU30` as in §3; `—` for other kinds. Rows are in line order.
- **Codes:** comma-separated `DR-xx`, `PR-xx`, `OI-xx`, `OI-xx.yy` or `N`; at least one per row. `Target or reason` is never empty: `N` rows give the reason, `OI` rows what is blocked or not built, other rows the target member or response field.
- **Reverse key** (§9.2): exactly the set `ReverseMatrixKeys.Generate()` yields.
  - Every public type of `Billing.Invoicing.Domain`, `Billing.Invoicing.Data` and `Billing.Invoicing.Api`, keyed `Namespace.Type`, and every public method each non-interface type declares, keyed `Namespace.Type.Method` (overloads share one key).
  - Excluded: constructors; property and event accessors and operators (special names); compiler-generated and record-synthesized members (names containing `<`, `Equals`, `GetHashCode`, `ToString`, `PrintMembers`, `Deconstruct`); methods declared on interfaces; fields, including `public const string` SQL texts and enum members.
  - One file key per `.ts` / `.tsx` module (not `.d.ts`) under `src/Billing.Invoicing.Web/ClientApp/src`, relative path without extension; and the host keys `Api/Program.cs` and `Web/Program.cs`.
- **Source construct(s):** DR / PR / OI ids with T / PU ids or legacy objects, or a reason starting `Infrastructure:`. An infrastructure row covers its type; each public method still has its own row.

### §9.1 Forward half (source → target)

| Key | Legacy id | Codes | Target or reason |
|---|---|---|---|
| Alert\|FORM\|ERR_ALERT\|-\|5 | — | N, OI-54 | UI mechanics: message alert used by `MESSAG`; realised by the error contract (`ProblemDetailsWriter.WriteAsync`, Web `FieldMessage`) |
| Alert\|FORM\|YES_NO\|-\|6 | — | N | UI mechanics: realised as the Save and Save & Print actions (T079) |
| Alert\|FORM\|DO_DEL\|-\|7 | — | DR-17 | Delete disabled: `InvoiceStatePolicy.CanDelete` |
| Alert\|FORM\|DISC_ALERT\|-\|8 | — | DR-06 | `DiscountLimitChoice` answered through `FinalDiscountLimitRule.ApplyChoice` |
| Alert\|FORM\|ABOUT\|-\|9 | — | N, OI-54 | UI mechanics: about box |
| Block\|FORM\|T_INV\|-\|10 | — | PR-13, DR-01, OI-15.01 | `InvoiceHeaderDraft`; `InvoiceQueries.GetInvoice` applies the block WHERE |
| Item\|T_INV\|INVTYPEID\|-\|11 | — | DR-20, OI-15.20 | `InvoiceHeaderDraft.InvTypeId`; list from `LookupQueries.GetInvoiceTypes` |
| Trigger\|T_INV\|INVTYPEID\|WHEN-VALIDATE-ITEM\|13 | T022 | DR-20, OI-33 | Home-care type in `InvoiceDefaultsRule.Apply`; `IS_STATE` not carried (OI-33) |
| Item\|T_INV\|INV_NO\|-\|15 | — | PR-09, DR-17 | `InvoiceHeaderDraft.InvNo`; number assigned inside `CreateFullInvoice` (UNVERIFIED) |
| Item\|T_INV\|INVDATE\|-\|16 | — | DR-20, DR-03, DR-10, PR-12 | `InvoiceHeaderDraft.DraftDate`, bound as `invdate` (D-39); `InvoiceHeaderDraft.InvDate` on a saved invoice |
| Item\|T_INV\|PATIENTNO\|-\|17 | — | DR-01, DR-03, PR-13 | `InvoiceHeaderDraft.PatientNo` |
| Trigger\|T_INV\|PATIENTNO\|WHEN-VALIDATE-ITEM\|18 | T023 | DR-03, DR-24, PR-24, OI-03, OI-24, OI-21 | `PatientEligibilityRules.Evaluate` over `LookupQueries.GetPatientCoverage`; pay type `PayTypeSelectionRule.Decide`; payer context PR-24 via `BIL_PATIENT_CONTEXT` (OI-03); `GET_PRICE_PLAN` not built (OI-24); pre-authorisation not derived (OI-21, D-52) |
| Item\|T_INV\|PATIENTNAME\|-\|20 | — | PR-24, OI-15.05, OI-15.01 | `PatientCoverageSnapshot.PatientName` on a draft; `InvoiceViewResponse.Display["PATIENTNAME"]` on a saved invoice |
| Trigger\|T_INV\|PATIENTNAME\|KEY-NEXT-ITEM\|21 | T024 | N | UI mechanics: focus navigation |
| Item\|T_INV\|INV_TIME\|-\|23 | — | OI-33, PR-12 | Not carried by the package inputs and not written by .NET; `InvoiceViewResponse.Display["INV_TIME"]` on a saved invoice |
| Trigger\|T_INV\|INV_TIME\|WHEN-VALIDATE-ITEM\|24 | T025 | PR-12 | Engine derives `PFLAG` (D-06, UNVERIFIED) |
| Item\|T_INV\|COMP_CODE\|-\|26 | — | DR-01, DR-24, PR-24 | `InvoiceHeaderDraft.CompCode` |
| Trigger\|T_INV\|COMP_CODE\|WHEN-VALIDATE-ITEM\|27 | T026 | DR-24, PR-04 | `PayTypeSelectionRule.Decide` with `LookupQueries.GetCompanyType`; direct share PR-04 |
| Item\|T_INV\|SUB_COMP_CODE\|-\|29 | — | PR-24, OI-32, OI-21 | `InvoiceHeaderDraft.SubCompCode`, read by `OpenItemGate.Evaluate` |
| Item\|T_INV\|SUB_COMP_NAME\|-\|30 | — | OI-15.13 | `SUB_COMPANY` rows of `LovResponse.Rows`; `PatientCoverageSnapshot.SubCompName`; `InvoiceViewResponse.Display["SUB_COMP_NAME"]` |
| Item\|T_INV\|CLASS_CODE\|-\|31 | — | DR-13, PR-04, OI-23 | `InvoiceHeaderDraft.ClassCode` |
| Item\|T_INV\|CLASS_NAME\|-\|32 | — | OI-15.14 | `THE_CLASS` rows of `LovResponse.Rows`; `PatientCoverageSnapshot.ClassName`; `InvoiceViewResponse.Display["CLASS_NAME"]` |
| Item\|T_INV\|PAYTYPE\|-\|33 | — | DR-24, DR-01, PR-24 | `InvoiceHeaderDraft.PayType`, set by `PayTypeSelectionRule.Decide` |
| Trigger\|T_INV\|PAYTYPE\|WHEN-VALIDATE-ITEM\|36 | T027 | PR-24, OI-03 | Payer context PR-24 (OI-03); item enablement is UI mechanics |
| Trigger\|T_INV\|PAYTYPE\|WHEN-LIST-CHANGED\|37 | T028 | N | UI mechanics: item enablement |
| Item\|T_INV\|CURR_CODE\|-\|39 | — | PR-15, OI-15.21 | `InvoiceHeaderDraft.CurrCode`; list from `LookupQueries.GetCurrencies` |
| Item\|T_INV\|DOCIDX\|-\|42 | — | DR-01, DR-10, DR-11, DR-25, PR-13 | `InvoiceHeaderDraft.DocId` |
| Trigger\|T_INV\|DOCIDX\|WHEN-VALIDATE-ITEM\|43 | T029 | DR-10, DR-11, DR-23, DR-25, PR-21, OI-32 | `DoctorSelectionRules.Validate`, `ClaimNumberRule.Build`, `AddToListRule.Derive`, `VisitLineRule.Choose` (service '2000' branch) + PR-21 `BilImportGateway.GetVisitLine`; clinic from the `DOC` LOV return; cash card blocked (OI-32); waiting counters display-only (D-18) |
| Trigger\|T_INV\|DOCIDX\|KEY-NEXT-ITEM\|44 | T030 | PR-21 | `BilImportGateway.GetVisitLine` through `ImportsController.VisitLine` |
| Item\|T_INV\|CLINICID\|-\|46 | — | DR-04, DR-05, DR-10, PR-13 | `InvoiceHeaderDraft.ClinicId` |
| Trigger\|T_INV\|CLINICID\|WHEN-VALIDATE-ITEM\|47 | T031 | DR-04, OI-22, OI-23 | `ClinicSuitabilityRules.CheckSex`; `CheckAge` needs `DAY_TO_DAYES` (OI-22); `payed_before` not built (OI-23) |
| Item\|T_INV\|DEPT_WISE\|-\|49 | — | DR-05, OI-33 | `InvoiceHeaderDraft.DeptWise`; only 0 is saved (D-53) |
| Trigger\|T_INV\|DEPT_WISE\|WHEN-VALIDATE-ITEM\|50 | T032 | DR-05 | `ErClinicRule.Validate` |
| Item\|T_INV\|CALL\|-\|52 | — | DR-05, OI-33 | `InvoiceHeaderDraft.Call`; only 0 is saved (D-53) |
| Trigger\|T_INV\|CALL\|WHEN-VALIDATE-ITEM\|53 | T033 | DR-05 | `ErClinicRule.Validate` |
| Item\|T_INV\|PRE_AUTHORIZATION\|-\|55 | — | OI-21, OI-32 | `InvoiceHeaderDraft.PreAuthorization`, bound null (D-52) |
| Item\|T_INV\|DOCID1\|-\|56 | — | OI-33 | `InvoiceHeaderDraft.DocId1`, display-only; not saved |
| Trigger\|T_INV\|DOCID1\|KEY-NEXT-ITEM\|57 | T034 | N | UI mechanics: focus navigation |
| Item\|T_INV\|ROOM_NO\|-\|59 | — | OI-33 | Not carried by the package inputs and not written by .NET; not displayed |
| Item\|T_INV\|DATEHEG\|-\|60 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|USER_NO\|-\|61 | — | OI-30 | `OperatorContext.UserNo` (header `X-His-User-No`), bound as `user_no`; `InvoiceHeaderDraft.UserNo` on a saved invoice |
| Item\|T_INV\|PAT_PAYX\|-\|62 | — | PR-07 | `PreviewTotals.PatPay` and `PreviewTotals.VatTotalPat` in `PreviewResponse.Totals` |
| Item\|T_INV\|COMP_PAY\|-\|63 | — | PR-04 | `PreviewTotals.CompPay`; `InvoiceViewResponse.Display["COMP_PAY"]` |
| Item\|T_INV\|APPROV_LIMIT\|-\|64 | — | PR-24 | Written by `insert_header` from the patient context (UNVERIFIED); not displayed |
| Item\|T_INV\|DISC_T\|-\|65 | — | DR-06, PR-06 | `InvoiceHeaderDraft.DiscT`; selects the bound final-discount field (D-41) |
| Trigger\|T_INV\|DISC_T\|WHEN-VALIDATE-ITEM\|68 | T035 | N | UI mechanics; the mode is `InvoiceHeaderDraft.DiscT` |
| Trigger\|T_INV\|DISC_T\|WHEN-LIST-CHANGED\|69 | T036 | N | UI mechanics; the mode is `InvoiceHeaderDraft.DiscT` |
| Trigger\|T_INV\|DISC_T\|WHEN-MOUSE-CLICK\|70 | T037 | N | UI mechanics; the mode is `InvoiceHeaderDraft.DiscT` |
| Item\|T_INV\|FINALDISC_PERC\|-\|72 | — | DR-06, PR-06 | `InvoiceHeaderDraft.FinalDiscPerc` |
| Trigger\|T_INV\|FINALDISC_PERC\|KEY-NEXT-ITEM\|73 | T038 | DR-22 | `HeaderRecordRules.ApplyPaymentTypeDefault`; navigation is UI mechanics |
| Trigger\|T_INV\|FINALDISC_PERC\|WHEN-VALIDATE-ITEM\|74 | T039 | DR-06, DR-07, PR-06 | Limit `FinalDiscountLimitRule.Evaluate` / `.ApplyChoice`; reset `PaymentAllocationRules.ResetAfterDiscountChange`; amount PR-06 |
| Item\|T_INV\|FINALDISC\|-\|76 | — | DR-06, PR-06 | `InvoiceHeaderDraft.FinalDisc` |
| Trigger\|T_INV\|FINALDISC\|KEY-NEXT-ITEM\|77 | T040 | DR-22 | `HeaderRecordRules.ApplyPaymentTypeDefault`; navigation is UI mechanics |
| Trigger\|T_INV\|FINALDISC\|WHEN-VALIDATE-ITEM\|78 | T041 | DR-06, DR-07, PR-06 | Limit `FinalDiscountLimitRule.Evaluate`; exceed check PR-06 (-20914, UNVERIFIED, legacy text via `OracleErrorCatalog`); reset `PaymentAllocationRules.ResetAfterDiscountChange` |
| Item\|T_INV\|AMOUNT_1\|-\|80 | — | DR-07, DR-08, DR-09, PR-08 | `InvoiceHeaderDraft.Amount1` |
| Trigger\|T_INV\|AMOUNT_1\|WHEN-VALIDATE-ITEM\|81 | T042 | DR-07 | `PaymentAllocationRules.AllocateSecondAmount` |
| Item\|T_INV\|AMOUNT_2\|-\|83 | — | DR-07, DR-08, DR-09 | `InvoiceHeaderDraft.Amount2` |
| Trigger\|T_INV\|AMOUNT_2\|WHEN-VALIDATE-ITEM\|84 | T043 | DR-08 | `PaymentAllocationRules.Refund` |
| Item\|T_INV\|SUB_PAYTYPE\|-\|86 | — | DR-20, DR-22, DR-08, OI-15.15 | `InvoiceHeaderDraft.SubPayType`; picker `PAY_TYPE1` |
| Trigger\|T_INV\|SUB_PAYTYPE\|WHEN-VALIDATE-ITEM\|87 | T044 | OI-15.15 | `PAY_TYPE1` LOV display through `LovQueries.PayTypes`; commented code not migrated (D-27) |
| Trigger\|T_INV\|SUB_PAYTYPE\|KEY-NEXT-ITEM\|88 | T045 | N | UI mechanics: focus navigation |
| Item\|T_INV\|SUB_PAYTYPE2\|-\|90 | — | DR-08, DR-22, OI-15.15 | `InvoiceHeaderDraft.SubPayType2`; picker `PAY_TYPE2` |
| Trigger\|T_INV\|SUB_PAYTYPE2\|KEY-NEXT-ITEM\|91 | T046 | N | UI mechanics: focus navigation |
| Item\|T_INV\|CASH_PAYED\|-\|93 | — | DR-08, OI-33 | `InvoiceHeaderDraft.CashPayed`; tendered cash not persisted (the engine stores `cash_collected`, UNVERIFIED) |
| Trigger\|T_INV\|CASH_PAYED\|KEY-NEXT-ITEM\|94 | T047 | N | UI mechanics: focus navigation |
| Trigger\|T_INV\|CASH_PAYED\|WHEN-VALIDATE-ITEM\|95 | T048 | N | Empty trigger |
| Item\|T_INV\|CASH_COLLECTED\|-\|97 | — | DR-09, DR-22 | `PreviewResponse.TotalCollected` from `PaymentAllocationRules.TotalCollected`; `InvoiceViewResponse.Display["TOTAL_COLLECTED"]` |
| Item\|T_INV\|DOC_NAME\|-\|98 | — | OI-15.16 | `DOC` rows of `LovResponse.Rows`; `InvoiceViewResponse.Display["DOC_NAME"]` |
| Item\|T_INV\|CARD_NAME\|-\|99 | — | OI-15.24 | `InvoiceViewResponse.Display["CARD_NAME"]` |
| Item\|T_INV\|DOC_NAME1\|-\|100 | — | OI-15.16, OI-33 | `InvoiceViewResponse.Display["DOC_NAME1"]`; the `DOC1` LOV is blocked |
| Item\|T_INV\|CLINICNAME\|-\|101 | — | OI-15.17 | `DOC` rows of `LovResponse.Rows`; `InvoiceViewResponse.Display["CLINICNAME"]` |
| Item\|T_INV\|COMP_NAME\|-\|102 | — | OI-15.12, OI-15.13 | `COMPANY1_2` rows of `LovResponse.Rows`; `PatientCoverageSnapshot.CompName`; `InvoiceViewResponse.Display["COMP_NAME"]` |
| Item\|T_INV\|PRINT_TIMES\|-\|103 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|AVANCE_PAYMENT\|-\|104 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|TOT_PAYMENT\|-\|105 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|MAX_DEDUCTABLE\|-\|106 | — | OI-23, PR-04, OI-15.05 | `PatientCoverageSnapshot.MaxDeductable`, read by `OpenItemGate.Evaluate` (D-51); `InvoiceViewResponse.Display["MAX_DEDUCTABLE"]` |
| Item\|T_INV\|BED_NO\|-\|107 | — | OI-33 | Not carried by the package inputs and not written by .NET; not displayed |
| Item\|T_INV\|USER_NAME_TO_SHOW\|-\|108 | — | OI-15.23 | `InvoiceViewResponse.Display["USER_NAME_TO_SHOW"]` |
| Item\|T_INV\|EDIT_USER_NAME_TO_SHOW\|-\|109 | — | OI-15.23 | `InvoiceViewResponse.Display["EDIT_USER_NAME_TO_SHOW"]` |
| Item\|T_INV\|ADMISSION_DATE\|-\|110 | — | OI-33 | Not carried by the package inputs and not written by .NET; not displayed |
| Item\|T_INV\|DISCHARGE_DATE\|-\|111 | — | OI-33 | Not carried by the package inputs and not written by .NET; not displayed |
| Item\|T_INV\|PHARMACY_INV_NO\|-\|112 | — | DR-01, OI-15.01 | `PHARMACY_INV_NO IS NULL` predicate of `InvoiceQueries.GetInvoice` and `.GetLastInvoiceNo` |
| Item\|T_INV\|PAYED_BEFORE\|-\|113 | — | OI-23 | Not built: `GET_PAYID_VALUE` is missing, so the value is not shown |
| Item\|T_INV\|REUND\|-\|114 | — | DR-08, OI-33 | `PreviewResponse.Refund` from `PaymentAllocationRules.Refund`; not persisted |
| Item\|T_INV\|THE_MONTH\|-\|115 | — | PR-12 | Written by `insert_header` (UNVERIFIED) |
| Item\|T_INV\|THE_YEAR\|-\|116 | — | PR-12 | Written by `insert_header` (UNVERIFIED) |
| Item\|T_INV\|PFLAG\|-\|117 | — | PR-12 | Derived by the engine (D-06, UNVERIFIED); `InvoiceViewResponse.Display["PFLAG"]` |
| Item\|T_INV\|MACHINE_N\|-\|121 | — | OI-30 | `OperatorContext.MachineName` (header `X-His-Machine`), bound as `machine_n`; `InvoiceHeaderDraft.MachineN` on a saved invoice |
| Item\|T_INV\|INIT_P_SEVER\|-\|122 | — | OI-33, OI-30 | Not carried by the package inputs and not written by .NET; filled from the unused report global `r_server` |
| Item\|T_INV\|IS_NEW\|-\|123 | — | N | UI mechanics: display flag read only by dead `NEW_WATING_NOXXXXX` |
| Item\|T_INV\|SHOW_RALA\|-\|124 | — | OI-15.18, OI-33 | Show reservations action: `GET /api/lov/RESERV_NO` through `LovQueries.ReservNo`, view-only |
| Trigger\|T_INV\|SHOW_RALA\|WHEN-BUTTON-PRESSED\|125 | T049 | OI-15.18, OI-33 | `LovQueries.ReservNo` (view-only); the chosen `SEQ_NO` is not persistable (OI-33) |
| Item\|T_INV\|SEQ_NO\|-\|127 | — | OI-33, OI-09 | `InvoiceHeaderDraft.SeqNo`, display-only; the queue number is the posting stage (UNVERIFIED) |
| Item\|T_INV\|UPD_USER_NO\|-\|128 | — | OI-33, OI-56 | `InvoiceViewResponse.Display["UPD_USER_NO"]`; update audit of a saved invoice is blocked |
| Item\|T_INV\|CLAIM_NO\|-\|129 | — | DR-10, DR-20 | `InvoiceHeaderDraft.ClaimNo` |
| Item\|T_INV\|SHIFT_SYSTEM_UNIQUE\|-\|130 | — | PR-10, OI-01 | Written by `insert_header` from the shift assert (UNVERIFIED); `FullInvoiceResultRow.ShiftSystemUnique` |
| Item\|T_INV\|G_NAME\|-\|131 | — | OI-15.13 | `InvoiceViewResponse.Display["G_NAME"]` |
| Item\|T_INV\|XGROUP\|-\|132 | — | PR-24 | Written by `insert_header` from the patient context (UNVERIFIED); group name shown as `G_NAME` |
| Item\|T_INV\|PAT_PAY\|-\|133 | — | PR-04 | `PreviewTotals.PatPay`; `InvoiceViewResponse.Display["PAT_PAY"]` |
| Item\|T_INV\|DHS_CLAIM_NO\|-\|134 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|TRANSCATION_STATUS\|-\|135 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|CLAIM_MAX_DAYES\|-\|136 | — | OI-33 | Not carried by the package inputs and not written by .NET; not displayed |
| Item\|T_INV\|DEL_BY\|-\|137 | — | DR-17, OI-33 | Delete audit column; invoice delete is refused |
| Item\|T_INV\|DELETE_NOTES\|-\|141 | — | DR-17, OI-33 | Delete audit column; invoice delete is refused |
| Item\|T_INV\|XRET\|-\|142 | — | N | UI mechanics: canvas switch, the Back action of Web `MoreDetailsScreen` |
| Trigger\|T_INV\|XRET\|WHEN-BUTTON-PRESSED\|143 | T050 | N | UI mechanics: canvas switch, realised as the Web `App` screen toggle |
| Item\|T_INV\|CARD_ID\|-\|145 | — | OI-32, OI-15.04 | Server-read `LookupQueries.GetPatientCardId` for `OpenItemGate.Evaluate` (D-52); `InvoiceViewResponse.Display["CARD_ID"]` |
| Item\|T_INV\|SHOW_WAIT\|-\|146 | — | N | UI mechanics: waiting counter, display-only (D-18) |
| Item\|T_INV\|A\|-\|147 | — | N | UI mechanics: waiting counter, display-only (D-18) |
| Item\|T_INV\|B\|-\|148 | — | N | UI mechanics: waiting counter, display-only (D-18) |
| Item\|T_INV\|C\|-\|149 | — | N | UI mechanics: waiting counter formula `:a-:b`, display-only (D-18) |
| Item\|T_INV\|DHS_ORG_CLAIM\|-\|150 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|LIST_ID\|-\|151 | — | PR-24, OI-24 | Price list per D-38: `EditablePreviewLine.ListId` or the `InvoiceQueries.GetClaimPreload` list; `InvoiceViewResponse.Display["LIST_ID"]` |
| Item\|T_INV\|PLAN_CODE\|-\|152 | — | OI-24, PR-24 | Not derived: `GET_PRICE_PLAN` is missing; `InvoiceViewResponse.Display["PLAN_CODE"]` |
| Item\|T_INV\|PLAN_NAME\|-\|153 | — | OI-15.25 | `InvoiceViewResponse.Display["PLAN_NAME"]` |
| Item\|T_INV\|LIST_NAME\|-\|154 | — | OI-15.26 | `InvoiceViewResponse.Display["LIST_NAME"]` |
| Item\|T_INV\|INS_NUMBER\|-\|155 | — | DR-20, PR-24, OI-15.05 | `MoreDetailsResponse.InsNumber`; on a draft `InvoiceHeaderDraft.InsNumber` from the claim preload until the patient is validated, then `PatientCoverageSnapshot.InsNumber` (D-87) |
| Item\|T_INV\|CARD_END\|-\|156 | — | DR-20, PR-24, DR-03, OI-15.05 | `MoreDetailsResponse.CardEnd`; on a draft `InvoiceHeaderDraft.CardEnd` from the claim preload until the patient is validated, then `PatientCoverageSnapshot.CardEnd` (D-87) |
| Item\|T_INV\|PAT_POLICY_NO\|-\|157 | — | DR-20, PR-24, OI-15.05 | `MoreDetailsResponse.PatPolicyNo`; on a draft `InvoiceHeaderDraft.PatPolicyNo` from the claim preload until the patient is validated, then `PatientCoverageSnapshot.PatPolicyNo` (D-87) |
| Item\|T_INV\|NOT_SEEN\|-\|158 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|T_INV\|RESERV_THE_TIME\|-\|159 | — | OI-33 | `InvoiceViewResponse.Display["RESERV_THE_TIME"]`; the `RESERV_NO` list is view-only |
| Item\|T_INV\|RESERV_THE_TIMEX\|-\|160 | — | OI-42 | Not built: reservation-time display field omitted (`GET_HTFN2` missing) |
| Item\|T_INV\|DEDUCT_TYPE\|-\|161 | — | PR-04, OI-33 | Deductible type inside the package share calculation (UNVERIFIED); not carried by the package inputs and not written by .net |
| Item\|T_INV\|ADD_TO_LIST\|-\|162 | — | DR-23 | `InvoiceHeaderDraft.AddToList`, set by `AddToListRule.Derive` |
| Item\|T_INV\|VAT_TOTAL_CO\|-\|163 | — | PR-05 | `PreviewTotals.VatTotalCo`; `InvoiceViewResponse.Display["VAT_TOTAL_CO"]` |
| Item\|T_INV\|VAT_TOTAL_PAT\|-\|164 | — | PR-05, PR-07 | `PreviewTotals.VatTotalPat`; `InvoiceViewResponse.Display["VAT_TOTAL_PAT"]` |
| Item\|T_INV\|VAT_TOTAL\|-\|165 | — | PR-05 | `FullInvoiceResultRow.VatTotal`; `InvoiceViewResponse.Display["VAT_TOTAL"]` |
| Item\|T_INV\|IS_STATE\|-\|166 | — | OI-33 | Not carried by the package inputs and not written by .NET |
| Item\|T_INV\|ROW_TYPE\|-\|167 | — | OI-15.01, OI-33 | `ROW_TYPE` filter of `InvoiceQueries.GetInvoice` and `.GetMoreDetails` (D-111) and predicate of `.GetLastInvoiceNo`; not written by the package (UNVERIFIED) |
| Item\|T_INV\|CLAIM_FLAG\|-\|168 | — | DR-10, DR-20 | `InvoiceHeaderDraft.ClaimFlag` |
| Item\|T_INV\|PRIORITY_VALUE\|-\|169 | — | OI-33 | Not carried by the package inputs and not written by .NET |
| Item\|T_INV\|BANK_TRANS_NO\|-\|170 | — | OI-33 | Not carried by the package inputs and not written by .NET |
| Item\|T_INV\|INFO_CENTER_ID\|-\|171 | — | OI-30, OI-15.01 | `OperatorContext.InfoCenterId` (header `X-His-Info-Center-Id`), bound as `info_center_id`; `InvoiceHeaderDraft.InfoCenterId` on a saved invoice |
| Item\|T_INV\|OFERID\|-\|172 | — | OI-33, DR-06 | `InvoiceHeaderDraft.OferId`, display-only; not saved |
| Trigger\|T_INV\|OFERID\|KEY-NEXT-ITEM\|173 | T051 | N | UI mechanics: focus navigation |
| Item\|T_INV\|OFFER_NAME\|-\|175 | — | OI-15.19, OI-33 | `OFFERS` rows of `LovResponse.Rows`; `InvoiceViewResponse.Display["OFFER_NAME"]` is null |
| Item\|T_INV\|NOTE_NO\|-\|176 | — | OI-15.01 | `InvoiceHeaderDraft.NoteNo`, bound as `note_no` |
| Item\|T_INV\|OLD_OR_NEW\|-\|177 | — | PR-11 | Derived and written by the engine (UNVERIFIED) |
| Item\|T_INV\|SUB_PAYTYPE_NAME\|-\|178 | — | OI-15.15 | `PAY_TYPE1` rows of `LovResponse.Rows`; `InvoiceViewResponse.Display["SUB_PAYTYPE_NAME"]` |
| Item\|T_INV\|SUB_PAYTYPE2_NAME\|-\|179 | — | OI-15.15 | `PAY_TYPE2` rows of `LovResponse.Rows`; `InvoiceViewResponse.Display["SUB_PAYTYPE2_NAME"]` |
| Relation\|T_INV\|T_INV_D_INV\|-\|360 | — | N, OI-15.01, OI-15.02 | UI mechanics: master-detail coordination; join `D_INV.INV_NO = T_INV.INV_NO` in `InvoiceQueries.GetInvoice` |
| Relation\|T_INV\|T_INV_T_INV_TRANS_M\|-\|361 | — | N, OI-15.01, OI-15.03 | UI mechanics: master-detail coordination; join `IMP_FROM_T_INV_NO = INV_NO` in `InvoiceQueries.GetMoreDetails` |
| Trigger\|T_INV\|T_INV\|POST-QUERY\|362 | T010 | DR-17, OI-15.01 | `InvoiceQueries.GetInvoice` (display lookups in `InvoiceViewResponse.Display`); read-only → `InvoiceStatePolicy.CanEdit` |
| Trigger\|T_INV\|T_INV\|PRE-INSERT\|363 | T011 | PR-09, PR-10, PR-11, PR-12, DR-21, OI-09, OI-19, OI-33 | Numbering PR-09; `OLD_OR_NEW` PR-11; month/year PR-12; shift PR-10 (the re-check only warned); queue number → queue posting (D-14, OI-09, UNVERIFIED); `IS_STATE` not carried (OI-33); PATIENT clear DR-21 `ReceptionTransferRule.ShouldClear` + `PatientTransferCommand.ClearReceptionTransfer` (failure swallowed, D-44) |
| Trigger\|T_INV\|T_INV\|PRE-QUERY\|364 | T012 | OI-15.01 | `ROW_TYPE` filter in `InvoiceQueries.GetInvoice` and `InvoiceQueries.GetMoreDetails`, from the fixed `LOCAL_DOC_TYPE` 505 (D-111) |
| Trigger\|T_INV\|T_INV\|PRE-UPDATE\|365 | T013 | OI-56 | Saved-invoice update blocked: `InvoicesController.Update` → 501 (D-36) |
| Trigger\|T_INV\|T_INV\|WHEN-VALIDATE-RECORD\|366 | T014 | DR-01, DR-22, PR-13 | `HeaderRecordRules.ValidateRecord`, `HeaderRecordRules.ApplyPaymentTypeDefault`; engine header checks re-check (UNVERIFIED) |
| Trigger\|T_INV\|T_INV\|WHEN-CREATE-RECORD\|367 | T015 | DR-20, DR-24, PR-12, OI-23 | `InvoiceDefaultsRule.Apply` (claim preload `InvoiceQueries.GetClaimPreload`, visit doctor `LookupQueries.GetVisitDoctor`, draft date `LookupQueries.GetDatabaseTime`, D-39); pay type `PayTypeSelectionRule.Decide`; `PFLAG` PR-12; `payed_before` not built (OI-23) |
| Trigger\|T_INV\|T_INV\|POST-INSERT\|368 | T016 | OI-13, OI-09, OI-12, OI-45 | Audit → engine `BIL_AUDIT` call (OI-13, UNVERIFIED); visit list → queue posting (OI-09, D-14, UNVERIFIED); SMS with the `inv_small_cash.jsp` link not sent (OI-12, OI-45, D-28) |
| Trigger\|T_INV\|T_INV\|POST-UPDATE\|369 | T017 | OI-56 | Saved-invoice update blocked: `InvoicesController.Update` → 501 (D-36) |
| Trigger\|T_INV\|T_INV\|ON-POPULATE-DETAILS\|370 | T018 | N | UI mechanics: the API returns header and lines together |
| Trigger\|T_INV\|T_INV\|PRE-DELETE\|371 | T019 | DR-17 | Header delete refused (`InvoiceStatePolicy.CanDelete`), so the detail delete never runs |
| Trigger\|T_INV\|T_INV\|ON-CHECK-DELETE-MASTER\|372 | T020 | DR-17 | `InvoiceStatePolicy.CanDelete`; no delete endpoint |
| Trigger\|T_INV\|T_INV\|ON-DELETE\|373 | T021 | DR-17 | `InvoiceStatePolicy.CanDelete`; no delete endpoint |
| Block\|FORM\|D_INV\|-\|375 | — | PR-14, OI-15.02 | `InvoiceLineDraft`; Web `InvoiceLinesGrid` |
| Item\|D_INV\|INS_EMP_NAME\|-\|376 | — | OI-15.27, OI-33 | `MoreDetailsResponse.Lines` `INS_EMP_NAME`, read-only |
| Item\|D_INV\|R_COUNT\|-\|377 | — | DR-02 | Line count passed to `InvoiceDetailRules.RequireDetails` |
| Item\|D_INV\|S_NET\|-\|378 | — | PR-02 | `PreviewTotals.TotalNet`; `InvoiceViewResponse.Display["TOTAL_NET"]` |
| Item\|D_INV\|XVAT_VAL_CO\|-\|379 | — | PR-05 | `PreviewTotals.VatTotalCo` |
| Item\|D_INV\|XVAT_VAL_PAT\|-\|380 | — | PR-05 | `PreviewTotals.VatTotalPat` |
| Item\|D_INV\|SERV_C\|-\|381 | — | DR-02 | Line count passed to `InvoiceDetailRules.RequireDetails` |
| Item\|D_INV\|INV_NO\|-\|382 | — | OI-15.02 | Join column of relation `T_INV_D_INV`; `InvoiceQueries.GetInvoice` lines bind `:invNo`; set by `insert_lines` (UNVERIFIED) |
| Item\|D_INV\|CATID\|-\|383 | — | OI-15.11 | `InvoiceLineDraft.CatId`, filled by `LovQueries.Cat`; not carried by `T_LINE_INPUT`, so `EditablePreviewLine.CatId` governs after a preview |
| Trigger\|D_INV\|CATID\|KEY-NEXT-ITEM\|384 | T064 | N | UI mechanics: focus navigation |
| Item\|D_INV\|XCAT_NAMEX\|-\|386 | — | OI-15.11 | `CAT` rows of `LovResponse.Rows`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `XCAT_NAMEX` |
| Item\|D_INV\|SERVICEID\|-\|387 | — | DR-12, DR-15, DR-16, DR-23, PR-14, OI-24 | `InvoiceLineDraft.ServiceId`, entered by id (the `SERVICES` LOV is blocked) |
| Trigger\|D_INV\|SERVICEID\|KEY-NEXT-ITEM\|388 | T065 | DR-07 | `PaymentAllocationRules.DefaultFirstAmount`; navigation is UI mechanics |
| Trigger\|D_INV\|SERVICEID\|WHEN-VALIDATE-ITEM\|389 | T066 | DR-15, DR-23, PR-16, PR-25, PR-04, PR-01, OI-32, OI-31 | `LineEntryRules.RequireService`; `AddToListRule.Derive`; standard-offer branch → PR-16, PR-25 inside the package (UNVERIFIED); share PR-04; untraced sub-rules → `OpenItemGate.Evaluate` blockers (OI-32); `PRICE` editability → price-override binding (D-42); consumption blocked (OI-31); recalculation PR-01 … PR-08 via `InvoiceWorkflowService.Preview` |
| Item\|D_INV\|SERVICEDESC\|-\|391 | — | OI-15.02 | `EditablePreviewLine.ServiceDesc`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `SERVICEDESC` |
| Item\|D_INV\|PRICE\|-\|392 | — | PR-01 | `InvoiceLineDraft.Price` (displayed) and `InvoiceLineDraft.PriceOverride` (bound only under D-42) |
| Trigger\|D_INV\|PRICE\|WHEN-VALIDATE-ITEM\|393 | T067 | PR-01 | Re-preview through `BilInvoiceApiGateway.CalculatePreview` |
| Item\|D_INV\|QTY\|-\|395 | — | DR-12, PR-01, PR-14 | `InvoiceLineDraft.Qty` |
| Trigger\|D_INV\|QTY\|WHEN-VALIDATE-ITEM\|396 | T068 | DR-12, OI-31 | `LineEntryRules.ValidateQuantity`; remaining quantity blocked (OI-31) |
| Trigger\|D_INV\|QTY\|KEY-NEXT-ITEM\|397 | T069 | N | UI mechanics: focus navigation |
| Item\|D_INV\|DISC\|-\|399 | — | PR-02, DR-13 | `InvoiceLineDraft.Disc` |
| Trigger\|D_INV\|DISC\|WHEN-VALIDATE-ITEM\|400 | T070 | PR-02 | Re-preview through `BilInvoiceApiGateway.CalculatePreview` |
| Item\|D_INV\|MY_DISC\|-\|402 | — | PR-02 | `InvoiceLineDraft.MyDisc` |
| Trigger\|D_INV\|MY_DISC\|WHEN-VALIDATE-ITEM\|403 | T071 | PR-02 | Re-preview through `BilInvoiceApiGateway.CalculatePreview` |
| Item\|D_INV\|FIXPAY\|-\|405 | — | PR-04, OI-33 | `InvoiceLineDraft.FixPay`, display-only; operator override not carried |
| Trigger\|D_INV\|FIXPAY\|WHEN-VALIDATE-ITEM\|406 | T072 | PR-04, OI-33 | Payer share inside the package (UNVERIFIED); operator override not carried (OI-33) |
| Item\|D_INV\|PAYRATE\|-\|408 | — | PR-04, OI-33 | `InvoiceLineDraft.PayRate`, display-only; operator override not carried |
| Trigger\|D_INV\|PAYRATE\|WHEN-VALIDATE-ITEM\|409 | T073 | PR-04, OI-33 | Payer share inside the package (UNVERIFIED); operator override not carried (OI-33) |
| Item\|D_INV\|S_PRICE\|-\|411 | — | PR-01 | `PreviewTotals.TotalGross`; `InvoiceViewResponse.Display["TOTAL_GROSS"]` |
| Item\|D_INV\|S_DISC\|-\|412 | — | PR-02 | `PreviewTotals.TotalDiscount`; `InvoiceViewResponse.Display["TOTAL_DISCOUNT"]` |
| Item\|D_INV\|XMY_NET\|-\|413 | — | PR-01, PR-02 | `EditablePreviewLine.MyNet` |
| Item\|D_INV\|XMY_PRICE\|-\|414 | — | PR-01 | `EditablePreviewLine.MyPrice` |
| Item\|D_INV\|CLINICNAME2\|-\|415 | — | N | UI mechanics: on no canvas and read by no trigger |
| Item\|D_INV\|NET\|-\|416 | — | PR-07, DR-07 | `PreviewTotals.CashCollected`, the amount due passed to `PaymentAllocationRules` (D-40) |
| Item\|D_INV\|S_PAY\|-\|417 | — | PR-04 | `PreviewTotals.PatPay` |
| Item\|D_INV\|D_INV_ROW_ID\|-\|418 | — | PR-20, OI-40.03 | Assigned by `insert_lines` from `D_INV_SEQ` (UNVERIFIED); `InvoiceViewResponse.Display["LINE_DISPLAY"]` `D_INV_ROW_ID` |
| Item\|D_INV\|SRV_STATE\|-\|419 | — | OI-15.02 | Written as 0 by `insert_lines` (UNVERIFIED); not displayed |
| Item\|D_INV\|MY_NET\|-\|420 | — | PR-02 | `EditablePreviewLine.MyNet`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `MY_NET` |
| Item\|D_INV\|THE_FIX\|-\|421 | — | PR-04 | `InvoiceViewResponse.Display["LINE_DISPLAY"]` `THE_FIX` |
| Item\|D_INV\|THE_RATE\|-\|422 | — | PR-04 | `InvoiceViewResponse.Display["LINE_DISPLAY"]` `THE_RATE` |
| Item\|D_INV\|THE_PAY\|-\|423 | — | PR-04 | `EditablePreviewLine.ThePay`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `THE_PAY` |
| Item\|D_INV\|MY_PRICE\|-\|424 | — | PR-01 | `EditablePreviewLine.MyPrice`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `MY_PRICE` |
| Item\|D_INV\|L_PAYED_BEFORE\|-\|425 | — | OI-23 | Not built: `GET_PAYID_VALUE` is missing |
| Item\|D_INV\|CURR_CODE\|-\|426 | — | PR-15 | `EditablePreviewLine.CurrCode`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `CURR_CODE` |
| Item\|D_INV\|TEETH_NO\|-\|427 | — | OI-15.02 | `InvoiceLineDraft.TeethNo`, bound as `teeth_no`; `MoreDetailsResponse.Lines` `TEETH_NO` |
| Item\|D_INV\|TOOTH_SURFACE\|-\|428 | — | OI-15.02 | `InvoiceLineDraft.ToothSurface`, bound as `tooth_surface`; `MoreDetailsResponse.Lines` `TOOTH_SURFACE` |
| Item\|D_INV\|REGULAR_LENSES_TYPE\|-\|429 | — | OI-33 | `InvoiceLineDraft.RegularLensesType`, display-only; `MoreDetailsResponse.Lines` `REGULAR_LENSES_TYPE` |
| Item\|D_INV\|LENS_SPECIFICATIONS\|-\|430 | — | OI-33 | `InvoiceLineDraft.LensSpecifications`, display-only; `MoreDetailsResponse.Lines` `LENS_SPECIFICATIONS` |
| Item\|D_INV\|CONTACT_LENSES_TYPE\|-\|431 | — | OI-33 | `InvoiceLineDraft.ContactLensesType`, display-only; `MoreDetailsResponse.Lines` `CONTACT_LENSES_TYPE` |
| Item\|D_INV\|F_L_INDICATOR\|-\|432 | — | OI-33 | `InvoiceLineDraft.FLIndicator`, display-only; `MoreDetailsResponse.Lines` `F_L_INDICATOR` |
| Item\|D_INV\|NUMBER_OF_PAIRS\|-\|433 | — | OI-33 | `InvoiceLineDraft.NumberOfPairs`, display-only; `MoreDetailsResponse.Lines` `NUMBER_OF_PAIRS` |
| Item\|D_INV\|TRANSCATION_STATUS\|-\|434 | — | OI-33 | Not carried by the package inputs and not written by .NET; on no canvas |
| Item\|D_INV\|PAT_SERV_REQ_ROW_ID\|-\|435 | — | PR-19, PR-20 | `InvoiceLineDraft.PatServReqRowId` |
| Item\|D_INV\|APPROV_DATE\|-\|436 | — | OI-15.02 | `InvoiceLineDraft.ApprovDate`, bound as `approv_date`; `MoreDetailsResponse.Lines` `APPROV_DATE` |
| Item\|D_INV\|APPROV_VALIDITY\|-\|437 | — | OI-15.02 | `InvoiceLineDraft.ApprovValidity`, bound as `approv_validity`; `MoreDetailsResponse.Lines` `APPROV_VALIDITY` |
| Item\|D_INV\|APPROV_REF_NO\|-\|438 | — | DR-14, DR-18 | `InvoiceLineDraft.ApprovRefNo` |
| Trigger\|D_INV\|APPROV_REF_NO\|WHEN-VALIDATE-ITEM\|439 | T074 | DR-14 | `LineEntryRules.ValidateApproval` |
| Item\|D_INV\|LIST_ID\|-\|441 | — | PR-24, OI-24 | `EditablePreviewLine.ListId` (D-38); `InvoiceViewResponse.Display["LINE_DISPLAY"]` `LIST_ID` |
| Item\|D_INV\|REQ_NEED_A\|-\|442 | — | DR-14, DR-18, PR-19 | `InvoiceLineDraft.ReqNeedA`, read-only |
| Item\|D_INV\|THE_COMP\|-\|447 | — | PR-04 | `EditablePreviewLine.TheComp`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `THE_COMP` |
| Item\|D_INV\|PKG_INV\|-\|448 | — | OI-31 | Not built: package consumption is blocked |
| Item\|D_INV\|CLAIM_NO\|-\|449 | — | DR-10 | `InvoiceLineDraft.ClaimNo` |
| Item\|D_INV\|TEETH_NO2\|-\|450 | — | OI-15.02 | `InvoiceLineDraft.TeethNo2`, bound as `teeth_no2` |
| Item\|D_INV\|VAT_RATE\|-\|451 | — | PR-05 | `EditablePreviewLine.VatRate`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `VAT_RATE` |
| Item\|D_INV\|VAT_VAL_CO\|-\|452 | — | PR-05 | `EditablePreviewLine.VatValCo`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `VAT_VAL_CO` |
| Item\|D_INV\|VAT_VAL_PAT\|-\|453 | — | PR-05 | `EditablePreviewLine.VatValPat`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `VAT_VAL_PAT` |
| Item\|D_INV\|VAT_VAL_PAT_EX\|-\|454 | — | PR-05 | `EditablePreviewLine.VatValPatEx`; `InvoiceViewResponse.Display["LINE_DISPLAY"]` `VAT_VAL_PAT_EX` |
| Item\|D_INV\|SFDA_CODE\|-\|455 | — | OI-15.02 | Written by `insert_lines` from the service (UNVERIFIED); not displayed |
| Item\|D_INV\|IMP_FROM_PKG\|-\|456 | — | PR-18, OI-33 | Superseded by `InvoiceLineDraft.PackageServiceId` / `PackageInstanceId` from `GET_PACKAGE_LINES`; the column is not written (UNVERIFIED) |
| Item\|D_INV\|REF_SERV_ROW\|-\|457 | — | PR-18, OI-33 | Superseded by `InvoiceLineDraft.PackageParentLineId`; the column is not written (UNVERIFIED) |
| Item\|D_INV\|INS_EMP\|-\|458 | — | OI-33, OI-15.27 | `InvoiceLineDraft.InsEmp`, display-only; `MoreDetailsResponse.Lines` `INS_EMP` |
| Item\|D_INV\|IS_DELETED\|-\|459 | — | OI-15.02 | Written by `insert_lines` (UNVERIFIED); saved-line deletion is blocked (OI-56) |
| Item\|D_INV\|INFO_CENTER_ID\|-\|460 | — | OI-30 | Written by `insert_lines` from the header `info_center_id` (`OperatorContext.InfoCenterId`), UNVERIFIED |
| Item\|D_INV\|REQ_A_STATUS\|-\|461 | — | DR-18, PR-19 | `InvoiceLineDraft.ReqAStatus`, read-only |
| Trigger\|D_INV\|D_INV\|WHEN-VALIDATE-RECORD\|559 | T052 | DR-16, PR-04 | `LineEntryRules.WarnNotRequested`; pay rate PR-04 |
| Trigger\|D_INV\|D_INV\|WHEN-NEW-RECORD-INSTANCE\|560 | T053 | N, OI-25 | UI mechanics: per-block permission toggling; permissions not built (OI-25) |
| Trigger\|D_INV\|D_INV\|WHEN-CREATE-RECORD\|561 | T054 | PR-04 | Payer share inside the package calls of preview and create (UNVERIFIED) |
| Trigger\|D_INV\|D_INV\|PRE-INSERT\|562 | T055 | PR-20, OI-40.03 | Engine `insert_lines` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:2794-2968] (UNVERIFIED) |
| Trigger\|D_INV\|D_INV\|POST-QUERY\|563 | T056 | OI-15.27 | `InvoiceQueries.GetMoreDetails` |
| Trigger\|D_INV\|D_INV\|PRE-UPDATE\|564 | T057 | OI-56 | Saved-line update blocked (D-36) |
| Trigger\|D_INV\|D_INV\|KEY-DELREC\|565 | T058 | PR-01, PR-02, PR-03, PR-04, PR-05, PR-06, PR-07, PR-08, OI-56 | Unsaved line: removed from the draft and re-previewed (`InvoiceWorkflowService.Preview`). Saved line of a just-saved invoice: blocked (OI-56, D-36) |
| Trigger\|D_INV\|D_INV\|PRE-DELETE\|566 | T059 | OI-56 | Saved-line deletion blocked (D-36) |
| Trigger\|D_INV\|D_INV\|POST-DELETE\|567 | T060 | OI-56 | Saved-line deletion blocked (D-36) |
| Trigger\|D_INV\|D_INV\|POST-INSERT\|568 | T061 | PR-20, OI-09, OI-31 | Link PR-20 inside `CreateFullInvoice`; locations → queue posting (OI-09, D-14, UNVERIFIED); package consumption blocked (OI-31) |
| Trigger\|D_INV\|D_INV\|KEY-CREREC\|569 | T062 | OI-56, DR-17 | Blocked on a saved invoice (D-36); unsaved drafts add lines freely (`InvoiceStatePolicy.CanEdit`) |
| Trigger\|D_INV\|D_INV\|POST-UPDATE\|570 | T063 | OI-56 | Saved-line update blocked (D-36) |
| Block\|FORM\|T_INV_TRANS_M\|-\|572 | — | DR-17, OI-15.03 | `InvoiceQueries.GetMoreDetails`, read-only |
| Item\|T_INV_TRANS_M\|TRANS_M_ROW_ID\|-\|573 | — | OI-15.03 | `MoreDetailsResponse.TransMRowIds` |
| Item\|T_INV_TRANS_M\|IMP_FROM_T_INV_NO\|-\|574 | — | OI-15.03 | `IMP_FROM_T_INV_NO = :invNo` predicate of `InvoiceQueries.GetMoreDetails` |
| Item\|T_INV_TRANS_M\|INV_TYPE\|-\|575 | — | OI-15.03 | `INV_TYPE = '197'` predicate of `InvoiceQueries.GetMoreDetails` |
| Item\|T_INV_TRANS_M\|THE_YEAR\|-\|576 | — | OI-15.03, OI-44 | Read by `InvoiceQueries.GetMoreDetails`, not returned; the T091 write is blocked |
| Item\|T_INV_TRANS_M\|INFO_CENTER_ID\|-\|577 | — | OI-15.03, OI-44 | Read by `InvoiceQueries.GetMoreDetails`, not returned; the T091 write is blocked |
| Trigger\|T_INV_TRANS_M\|T_INV_TRANS_M\|PRE-QUERY\|713 | T075 | OI-15.03 | `INV_TYPE = '197'` filter in `InvoiceQueries.GetMoreDetails` |
| Block\|FORM\|TOOL\|-\|715 | — | N | Control block; each button has its own Item row |
| Item\|TOOL\|TODAY_DATE\|-\|716 | — | DR-20 | Database date: `LookupQueries.GetDatabaseTime` into `DraftDate` (D-39) |
| Item\|TOOL\|FORM_TITLE\|-\|717 | — | N | UI mechanics: title |
| Item\|TOOL\|IS_ADMINX\|-\|718 | — | N, OI-25 | UI mechanics: admin-mode display; permissions not built |
| Item\|TOOL\|L_STATUS\|-\|719 | — | N | UI mechanics: status display written only by commented code (D-27) |
| Item\|TOOL\|DO_PREVIEW\|-\|720 | — | N, OI-11 | UI mechanics: print-to-file flag; printing blocked |
| Trigger\|TOOL\|DO_PREVIEW\|WHEN-CHECKBOX-CHANGED\|721 | T076 | N, OI-11 | UI mechanics: print flag; printing blocked (OI-11, OI-45, OI-46) |
| Item\|TOOL\|IN_OUT_PHARAMACY\|-\|723 | — | N | UI mechanics: filled by the `CAT` LOV, read by no trigger |
| Item\|TOOL\|LDISCT\|-\|724 | — | DR-13, PR-02 | `InvoiceLineDraft.DiscountType` (R / V) |
| Trigger\|TOOL\|LDISCT\|WHEN-RADIO-CHANGED\|727 | T077 | DR-13 | `LineEntryRules.ValidateDiscountType` |
| Trigger\|TOOL\|LDISCT\|WHEN-VALIDATE-ITEM\|728 | T078 | DR-13 | `LineEntryRules.ValidateDiscountType` |
| Item\|TOOL\|XUSERS_PREV\|-\|730 | — | OI-25 | Not built: per-user privilege display |
| Item\|TOOL\|XCOMP_TYPE\|-\|731 | — | DR-24, OI-15.13 | `LookupQueries.GetCompanyType` → `PayTypeSelectionRule.Decide` |
| Item\|TOOL\|CL_OVER\|-\|732 | — | N | UI mechanics: read by no trigger |
| Item\|TOOL\|XCOMP_TYPE0000\|-\|733 | — | N | UI mechanics: read by no trigger |
| Item\|TOOL\|IS_LOCKED\|-\|734 | — | N | UI mechanics: lock display written only by commented code (D-27) |
| Item\|TOOL\|PHARM_ID\|-\|735 | — | N | UI mechanics: filled from `PREF` 501 by T003, read by no rule |
| Item\|TOOL\|DO_PRINT\|-\|736 | — | PR-22, OI-11 | Save and Save & Print actions: `InvoicesController.Create`, then `InvoicesController.Documents` (501) |
| Trigger\|TOOL\|DO_PRINT\|WHEN-BUTTON-PRESSED\|737 | T079 | PR-22, PR-23, OI-11, OI-45, OI-46, OI-49, OI-56 | `InvoicesController.Create` → `InvoiceWorkflowService.Create`; print part blocked (`InvoicesController.Documents`, OI-11, OI-45, OI-46, OI-49); post-save edit blocked (OI-56) |
| Item\|TOOL\|PUSH_BUTTON1161\|-\|739 | — | OI-47 | Patient card action: `documents/patient-card` → 501 |
| Trigger\|TOOL\|PUSH_BUTTON1161\|WHEN-BUTTON-PRESSED\|740 | T080 | OI-47 | Blocked: `LegacyExternalCalls.BuildLegacyDocument` (`documents/patient-card` → 501) |
| Item\|TOOL\|PUSH_BUTTON1199\|-\|742 | — | OI-12, OI-45 | Send SMS action: `InvoicesController.Sms` → 501 |
| Trigger\|TOOL\|PUSH_BUTTON1199\|WHEN-BUTTON-PRESSED\|743 | T081 | OI-12, OI-26, OI-45 | Blocked: `InvoicesController.Sms` → 501 (D-28) |
| Item\|TOOL\|GO_LAST\|-\|745 | — | OI-15.01 | Last invoice action: `InvoicesController.Last` |
| Trigger\|TOOL\|GO_LAST\|WHEN-BUTTON-PRESSED\|746 | T082 | OI-15.01 | `InvoiceQueries.GetLastInvoiceNo` (same predicates) through `InvoicesController.Last` |
| Item\|TOOL\|RELASE\|-\|748 | — | OI-23 | Not rendered: Release deductible needs `GET_PAYID_VALUE` |
| Trigger\|TOOL\|RELASE\|WHEN-BUTTON-PRESSED\|749 | T083 | OI-23 | Not built: `GET_PAYID_VALUE` is missing, so Release is not rendered |
| Item\|TOOL\|PUSH_BUTTON1062\|-\|751 | — | OI-50 | Not rendered: opens another Form |
| Trigger\|TOOL\|PUSH_BUTTON1062\|WHEN-BUTTON-PRESSED\|752 | T084 | OI-50 | Not built: other Form, out of scope |
| Item\|TOOL\|DO_IMP2\|-\|754 | — | DR-18, PR-19 | Import requests action: `ImportsController.Requests` |
| Trigger\|TOOL\|DO_IMP2\|WHEN-BUTTON-PRESSED\|755 | T085 | DR-18, PR-19, PR-16, OI-15.08 | `RequestImportRules.RequireDoctor`, `RequestImportRules.Notices` (D-26); `InvoiceQueries.GetSelectedRequestRows`; `BilImportGateway.ImportRequestLines` with approval mode 0 / 1 (D-12); standard offer on imported cash lines → PR-16 inside the package at preview and create (UNVERIFIED) |
| Item\|TOOL\|PUSH_BUTTON934\|-\|757 | — | N | UI mechanics: canvas switch, the More details action of Web `App` |
| Trigger\|TOOL\|PUSH_BUTTON934\|WHEN-BUTTON-PRESSED\|758 | T086 | N | UI mechanics: canvas switch, realised as the Web `App` screen toggle |
| Item\|TOOL\|CMD_LANG\|-\|760 | — | N | UI mechanics: language toggle, not rendered (D-18) |
| Trigger\|TOOL\|CMD_LANG\|WHEN-BUTTON-PRESSED\|761 | T087 | N | UI mechanics: single-language UI (D-18) |
| Item\|TOOL\|DO_TRANSLATE\|-\|763 | — | OI-51 | Not rendered: opens another Form |
| Trigger\|TOOL\|DO_TRANSLATE\|WHEN-BUTTON-PRESSED\|764 | T088 | OI-51 | Not built: other Form, out of scope |
| Item\|TOOL\|LIST1111\|-\|766 | — | OI-45, OI-46, OI-49 | Not rendered: report choice of the blocked invoice print |
| Item\|TOOL\|IMP_PKG\|-\|770 | — | PR-18, DR-19 | Import package action: `ImportsController.Package` |
| Trigger\|TOOL\|IMP_PKG\|WHEN-BUTTON-PRESSED\|771 | T089 | PR-18, DR-19, DR-23 | `BilInvoiceApiGateway.GetPackageLines` (price list per D-38); `PackageImportRules.Evaluate` (warning); `AddToListRule.Derive` |
| Item\|TOOL\|ITEM1114\|-\|773 | — | OI-52 | Not rendered: opens another Form |
| Trigger\|TOOL\|ITEM1114\|WHEN-BUTTON-PRESSED\|774 | T090 | OI-52 | Not built: other Form, out of scope |
| Item\|TOOL\|PUSH_BUTTON1123\|-\|776 | — | OI-10, OI-44 | Store transfer action: `InvoicesController.StockTransfer` → 501 |
| Trigger\|TOOL\|PUSH_BUTTON1123\|WHEN-BUTTON-PRESSED\|777 | T091 | OI-10, OI-44 | Blocked: `InvoicesController.StockTransfer` → 501 (`LegacyExternalCalls.TransferStock`, D-46) |
| Item\|TOOL\|ITEM1124\|-\|779 | — | OI-28 | Not rendered: opens another Form |
| Trigger\|TOOL\|ITEM1124\|WHEN-BUTTON-PRESSED\|780 | T092 | OI-28 | Not built: other Form, out of scope |
| Item\|TOOL\|ITEM1151\|-\|782 | — | PR-17 | Load offer action: `ImportsController.BundledOffer` |
| Trigger\|TOOL\|ITEM1151\|WHEN-BUTTON-PRESSED\|783 | T093 | PR-17 | `BilInvoiceApiGateway.GetBundledOfferLines` through `ImportsController.BundledOffer` |
| Item\|TOOL\|P_PRINT\|-\|785 | — | OI-48 | Iqama check action: `documents/iqama-check` → 501 |
| Trigger\|TOOL\|P_PRINT\|WHEN-BUTTON-PRESSED\|786 | T094 | OI-48 | Blocked: `LegacyExternalCalls.BuildLegacyDocument` (`documents/iqama-check` → 501) |
| Item\|TOOL\|ITEM1200\|-\|788 | — | OI-47, OI-26 | Barcode SMS action: `documents/barcode-sms` → 501 |
| Trigger\|TOOL\|ITEM1200\|WHEN-BUTTON-PRESSED\|789 | T095 | OI-47, OI-26 | Blocked: `LegacyExternalCalls.BuildLegacyDocument` (`documents/barcode-sms` → 501) |
| Block\|FORM\|TITLE\|-\|792 | — | N | Control block with no items |
| Canvas\|FORM\|CANVAS2\|-\|793 | — | N, OI-55 | UI container, realised as Web `InvoiceScreen` |
| Canvas\|FORM\|MORE\|-\|810 | — | N, OI-55 | UI container, realised as Web `MoreDetailsScreen` |
| ModuleParameter\|FORM\|IS_HOME_CARE\|-\|814 | — | DR-20 | `InvoiceEntryParameters.IsHomeCare` → `InvoiceDefaultsRule.Apply` |
| ModuleParameter\|FORM\|NEW_PAT_INV\|-\|815 | — | N | Unused |
| ModuleParameter\|FORM\|THE_DOC\|-\|816 | — | DR-11 | `InvoiceEntryParameters.TheDoc` → `DoctorSelectionRules.Validate` |
| ModuleParameter\|FORM\|COMP_TYPE\|-\|817 | — | PR-04 | `InvoiceEntryParameters.CompType`; line payer rate is payer share inside the package (UNVERIFIED) |
| ModuleParameter\|FORM\|PAY_VAT_CO\|-\|818 | — | PR-05 | `InvoiceEntryParameters.PayVatCo`; VAT flags inside the package (UNVERIFIED) |
| ModuleParameter\|FORM\|FROM_CHK\|-\|819 | — | N | UI mechanics: item toggling |
| ModuleParameter\|FORM\|CLAIM_FLAG\|-\|820 | — | DR-10, DR-20, DR-25, OI-24 | `InvoiceEntryParameters.ClaimFlag` → `ClaimNumberRule.Build`, `InvoiceDefaultsRule.Apply`; also filters the blocked `SERVICES` LOV |
| ModuleParameter\|FORM\|CASH_OR_CREDIT\|-\|821 | — | DR-24 | `InvoiceEntryParameters.CashOrCredit` → `PayTypeSelectionRule.Decide` |
| ModuleParameter\|FORM\|DO_REVIEW\|-\|822 | — | DR-25 | `InvoiceEntryParameters.DoReview` → `VisitLineRule.Choose` |
| ModuleParameter\|FORM\|THE_COUNTRY\|-\|823 | — | N | Unused |
| ModuleParameter\|FORM\|CLAIM_DATE\|-\|824 | — | N | Unused |
| ModuleParameter\|FORM\|PAY_VAT\|-\|825 | — | PR-05 | `InvoiceEntryParameters.PayVat`; VAT flags inside the package (UNVERIFIED) |
| ModuleParameter\|FORM\|ONE_VISIT_960\|-\|826 | — | OI-32 | `InvoiceEntryParameters.OneVisit960`; claim / revisit sub-rule blocked |
| ModuleParameter\|FORM\|DIRECT_CALL\|-\|827 | — | N | UI mechanics: item toggling |
| ModuleParameter\|FORM\|X422_APPROV_CHECK\|-\|828 | — | DR-14, DR-18, PR-19 | `InvoiceEntryParameters.X422ApprovCheck`; the workflow uses the PREF 422 value instead, 1 when it holds no integer (D-73) → `LineEntryRules.ValidateApproval`, `RequestImportRules.Notices`, `BilImportGateway.ApprovalCheckMode` (D-12) |
| ModuleParameter\|FORM\|DEDUCT_RATE\|-\|829 | — | N | Unused |
| ModuleParameter\|FORM\|DEDUCT_FIXED\|-\|830 | — | N | Unused |
| ModuleParameter\|FORM\|DIRECT_COMP_SHARE\|-\|831 | — | PR-04 | `InvoiceEntryParameters.DirectCompShare`; payer share inside the package (UNVERIFIED) |
| ModuleParameter\|FORM\|LOCAL_DOC_TYPE\|-\|832 | — | OI-15.01, OI-30, N | `InvoiceEntryParameters.LocalDocType`, server-owned and fixed at 505 → `ROW_TYPE` filter of `InvoiceQueries.GetInvoice` and `InvoiceQueries.GetMoreDetails` (T012, D-111); UI uses not migrated |
| ModuleParameter\|FORM\|VISIT_UNIQUE\|-\|833 | — | DR-20, PR-19 | `InvoiceEntryParameters.VisitUnique` → `InvoiceDefaultsRule.Apply`, `InvoiceQueries.GetSelectedRequestRows` |
| ModuleParameter\|FORM\|NEW_DOC\|-\|834 | — | DR-20 | `InvoiceEntryParameters.NewDoc` → doctor for claim parameter '1' |
| ModuleParameter\|FORM\|WILL_DO_IMP\|-\|835 | — | N | UI mechanics: item toggling |
| ModuleParameter\|FORM\|INV_ADMIN\|-\|836 | — | OI-01 | `InvoiceEntryParameters.InvAdmin`; shift bypass not reproduced (D-25); T027 item toggling is UI mechanics |
| ModuleParameter\|FORM\|INV_DATE_ADMIN\|-\|837 | — | DR-03 | `InvoiceEntryParameters.InvDateAdmin` → `PatientEligibilityRules.Evaluate`; the workflow sets it to 2 (normal user) for every request (D-98) |
| ModuleParameter\|FORM\|OPEN_FROM_ACC\|-\|838 | — | N | Unused |
| ModuleParameter\|FORM\|PKG_INV\|-\|839 | — | OI-31 | `InvoiceEntryParameters.PkgInv`; package consumption blocked: `PackageConsumptionGateway.Begin` → 501 |
| ModuleParameter\|FORM\|CLAIM_NO\|-\|840 | — | DR-10, DR-20, DR-25 | `InvoiceEntryParameters.ClaimNo` → `ClaimNumberRule.Build`, `InvoiceDefaultsRule.Apply`, `VisitLineRule.Choose` |
| ModuleParameter\|FORM\|PHARAMACY_INSTALL_601\|-\|841 | — | N | Unused |
| ModuleParameter\|FORM\|RESERV_BY_TIME_801\|-\|842 | — | N | Reservation-by-time not migrated: `RESERV_NO` is view-only |
| ModuleParameter\|FORM\|SHIFT_CONYTOL_901\|-\|843 | — | OI-01 | `InvoiceEntryParameters.ShiftConytol901`; shift switch not reproduced (D-25) |
| ModuleParameter\|FORM\|LESS_PAYMENT_970\|-\|844 | — | N | Unused (legacy anomaly, §10) |
| ModuleParameter\|FORM\|P_USER\|-\|845 | — | N | Unused |
| LOV\|FORM\|APPROVED_SERV\|-\|846 | — | N | Unused by the blocks: attached to no item |
| LOV\|FORM\|RESERV_NO\|-\|859 | — | OI-15.18, OI-33 | `LovQueries.ReservNo`, view-only: `SEQ_NO` is not saved; lists `RESERV_NO`, `THE_TIME` and `PATAINTNO` only (D-105), for an active doctor of the operator's centre (D-106) |
| LOV\|FORM\|CAT\|-\|866 | — | OI-15.11 | `LovQueries.Cat` |
| LOV\|FORM\|COMPANY1_2\|-\|871 | — | OI-15.12 | `LovQueries.Company` |
| LOV\|FORM\|THE_CLASS\|-\|876 | — | OI-15.14 | `LovQueries.TheClass`; rows only for a sub-company of a company of the operator's centre (D-106) |
| LOV\|FORM\|SUB_COMPANY\|-\|880 | — | OI-15.13 | `LovQueries.SubCompany`; rows only for a company of the operator's centre (D-106) |
| LOV\|FORM\|DOC1\|-\|884 | — | OI-33 | Blocked: `GET /api/lov/DOC1` → 501, `DOCID1` is not saved (D-49) |
| LOV\|FORM\|DOC\|-\|888 | — | OI-15.16, OI-15.17 | `LovQueries.Doc` |
| LOV\|FORM\|CLINICS\|-\|894 | — | N | Unused by the blocks: attached to no item |
| LOV\|FORM\|PATIENT\|-\|898 | — | N | Unused by the blocks: the `PATIENTNO` item has an empty `LovName` |
| LOV\|FORM\|PATIENT_TRANS\|-\|902 | — | N | Unused by the blocks: opened only by dead `IMP_RXXX` |
| LOV\|FORM\|SERVICES\|-\|906 | — | OI-24 | Blocked: `GET /api/lov/SERVICES` → 501, `PLAN_CODE` / `LIST_ID` come only from the missing `GET_PRICE_PLAN` (D-49) |
| LOV\|FORM\|OFFERS\|-\|917 | — | OI-15.19 | `LovQueries.Offers` |
| LOV\|FORM\|PAY_TYPE1\|-\|921 | — | OI-15.15 | `LovQueries.PayTypes`, returning only `PAY_TYPE_ID` and `PAY_TYPE_NAME` (D-105); fills `SubPayType` only |
| LOV\|FORM\|PAY_TYPE2\|-\|925 | — | OI-15.15 | `LovQueries.PayTypes`, returning only `PAY_TYPE_ID` and `PAY_TYPE_NAME` (D-105); fills `SubPayType2` |
| ProgramUnit\|FORM\|CHK_SEC_DETAIL\|-\|960 | PU01 | N, OI-25 | UI mechanics: item toggling; permissions not built (OI-25) |
| ProgramUnit\|FORM\|CHK_VOL\|-\|961 | PU02 | N | Dead code: never called (client-machine behaviour) |
| ProgramUnit\|FORM\|DO_INTERFACE\|-\|962 | PU03 | N | UI mechanics: layout direction |
| ProgramUnit\|FORM\|DISABLE_AN_ITEM\|-\|963 | PU04 | N | UI mechanics: item enablement |
| ProgramUnit\|FORM\|HIDE_AN_ITEM\|-\|964 | PU05 | N | UI mechanics: item visibility |
| ProgramUnit\|FORM\|CHK_TIME\|-\|965 | PU06 | N | Dead code: never called |
| ProgramUnit\|FORM\|CHK_SEC\|-\|966 | PU07 | OI-25 | Not built: no authorisation in this run |
| ProgramUnit\|FORM\|MESSAG\|-\|967 | PU08 | N | UI mechanics: alert display, replaced by the error contract (`ProblemDetailsWriter.WriteAsync`, `MessageDto`) |
| ProgramUnit\|FORM\|HIDE_AN_ITEM2\|-\|968 | PU09 | N | Dead code: never called |
| ProgramUnit\|FORM\|SMALL_CALC\|-\|969 | PU10 | PR-01, PR-02, PR-03, PR-04, PR-05, PR-06, PR-07, PR-08, OI-23 | `BilInvoiceApiGateway.CalculatePreview`; the `payed_before` part of the cap is blocked (OI-23, D-51) |
| ProgramUnit\|FORM\|IMP_RXXX\|-\|970 | PU11 | N | Dead code: never called |
| ProgramUnit\|FORM\|CHECK_PACKAGE_FAILURE\|-\|971 | PU12 | N | UI mechanics: master-detail coordination |
| ProgramUnit\|FORM\|QUERY_MASTER_DETAILS\|-\|972 | PU13 | N | UI mechanics: master-detail coordination |
| ProgramUnit\|FORM\|CLEAR_ALL_MASTER_DETAILS\|-\|973 | PU14 | N | UI mechanics: master-detail coordination |
| ProgramUnit\|FORM\|NEW_WATING_NOXXXXX\|-\|974 | PU15 | N | Dead code: never called |
| ProgramUnit\|FORM\|MAKE_CASH\|-\|975 | PU16 | DR-24, PR-24, OI-03, OI-24 | Pay type `PayTypeSelectionRule.Decide`; payer context PR-24 (OI-03); price plan not built (OI-24) |
| ProgramUnit\|FORM\|DO_DISC\|-\|976 | PU17 | PR-02, DR-13 | Engine discount calculation (UNVERIFIED); its message is covered by `LineEntryRules.ValidateDiscountType` |
| ProgramUnit\|FORM\|GET_NOTES\|-\|977 | PU18 | OI-09 | Queue posting stage (UNVERIFIED) |
| ProgramUnit\|FORM\|OKA\|-\|978 | PU19 | PR-03, PR-04, DR-23, OI-31, OI-32 | Package pricing context: discountability PR-03 and payer share PR-04 inside the package (UNVERIFIED); `AddToListRule.Derive` (D-37); untraced sub-rules → `OpenItemGate.Evaluate` blockers (OI-32); consumption blocked (OI-31); no offer branch: standard offers are traced to T066 and T085 |
| ProgramUnit\|FORM\|ROUND_FOR_CASH\|-\|979 | PU20 | PR-07 | Engine `cash_collected` (UNVERIFIED) |
| ProgramUnit\|FORM\|CHG_PRMPT2\|-\|980 | PU21 | N, OI-43 | UI mechanics: prompt translation |
| ProgramUnit\|FORM\|CHK_LIC_SEC\|-\|981 | PU22 | N | Dead code: never called |
| ProgramUnit\|FORM\|CHK_ADV_CLASS\|-\|982 | PU23 | PR-04, OI-23, OI-32 | Share and advanced classes PR-04; advanced-class deductible blocked (OI-23, D-51); cash card blocked (OI-32) |
| ProgramUnit\|FORM\|A_HIDE_SHOW\|-\|983 | PU24 | N | UI mechanics: item visibility |
| ProgramUnit\|FORM\|DO_PRINT\|-\|984 | PU25 | OI-11, OI-45, OI-46, OI-49 | Blocked: `BilInvoiceApiGateway.BuildPrintUrl`; PDF export and printer routing not migrated (client-machine behaviour) |
| ProgramUnit\|FORM\|REF_TIME\|-\|985 | PU26 | OI-42 | Not built: display field omitted |
| ProgramUnit\|FORM\|CAN_GO_ITEM\|-\|986 | PU27 | N | Dead code: never called |
| ProgramUnit\|FORM\|DO_NEW_RECORD\|-\|987 | PU28 | N | Dead code: never called |
| ProgramUnit\|FORM\|PRINT_URL\|-\|988 | PU29 | N | Dead code: never called |
| ProgramUnit\|FORM\|REMAIN\|-\|989 | PU30 | DR-08 | `PaymentAllocationRules.Refund` |
| RecordGroup\|FORM\|PAY_TYPE\|-\|994 | — | OI-15.15 | SQL of `LovQueries.PayTypes` (`:global.lang` bound as 'E', D-49), projected to `PAY_TYPE_ID` and `PAY_TYPE_NAME` (D-105) |
| RecordGroup\|FORM\|RECORD_GROUP1196\|-\|1001 | — | N | Not referenced by any LOV |
| RecordGroup\|FORM\|PATIENT_TRANS\|-\|1008 | — | N | Behind an LOV unused by the blocks |
| RecordGroup\|FORM\|CAT\|-\|1012 | — | OI-15.11 | SQL of `LovQueries.Cat` |
| RecordGroup\|FORM\|THE_CLASS\|-\|1017 | — | OI-15.14 | SQL of `LovQueries.TheClass`, limited to sub-companies of the operator's centre's companies (D-106) |
| RecordGroup\|FORM\|COMPANY\|-\|1021 | — | OI-15.12 | SQL of `LovQueries.Company` |
| RecordGroup\|FORM\|SUB_COMP\|-\|1026 | — | OI-15.13 | SQL of `LovQueries.SubCompany`, limited to companies of the operator's centre (D-106) |
| RecordGroup\|FORM\|DOC1\|-\|1030 | — | OI-33 | Not executed: the `DOC1` LOV returns 501 (D-49) |
| RecordGroup\|FORM\|SERVICES\|-\|1034 | — | OI-24, OI-15.09, OI-40.04 | Not executed: needs `PLAN_CODE` / `LIST_ID` from the missing `GET_PRICE_PLAN` (D-49) |
| RecordGroup\|FORM\|SERVICES_BAK\|-\|1045 | — | N | Not referenced by any LOV |
| RecordGroup\|FORM\|PATIENT\|-\|1055 | — | N | Behind an LOV unused by the blocks |
| RecordGroup\|FORM\|CLINICS\|-\|1059 | — | N | Behind an LOV unused by the blocks |
| RecordGroup\|FORM\|DOC\|-\|1063 | — | OI-15.16, OI-15.17 | SQL of `LovQueries.Doc` |
| RecordGroup\|FORM\|RESERV_NO\|-\|1069 | — | OI-15.18 | SQL of `LovQueries.ReservNo` (`:global.reserv_system_500` bound as 0, D-49), projected to `RESERV_NO`, `THE_TIME` and `PATAINTNO` (D-105) and limited to active doctors of the operator's centre (D-106) |
| RecordGroup\|FORM\|APPROVED_SERV\|-\|1076 | — | N, OI-41.20 | Behind an LOV unused by the blocks |
| RecordGroup\|FORM\|OFFERS\|-\|1089 | — | OI-15.19 | SQL of `LovQueries.Offers` |
| Report\|FORM\|XX\|-\|1093 | — | OI-27 | Not built: `nat.rdf` is absent and no trigger runs the report object |
| Trigger\|FORM\|FORM\|WHEN-WINDOW-CLOSED\|1094 | T001 | N | UI mechanics: window close |
| Trigger\|FORM\|FORM\|POST-FORM\|1095 | T002 | N | Empty trigger |
| Trigger\|FORM\|FORM\|WHEN-NEW-FORM-INSTANCE\|1096 | T003 | PR-10, OI-01, OI-30, OI-25, DR-14, DR-20, OI-24, OI-15.22 | Shift gate → PR-10 inside `BilInvoiceApiGateway.CreateFullInvoice`; switch and admin bypass not reproduced (OI-01, D-25); system lock not built (OI-30); permission not built (OI-25); PREF 422 → `LookupQueries.GetPreferences` feeding DR-14; `GET_PRICE_PLAN` blocked (OI-24); preload → DR-20 `InvoiceDefaultsRule.Apply` |
| Trigger\|FORM\|FORM\|PRE-FORM\|1097 | T004 | OI-15.20, OI-15.21 | `LookupQueries.GetInvoiceTypes`, `LookupQueries.GetCurrencies` through `LookupsController.InvoiceTypes` / `.Currencies` |
| Trigger\|FORM\|FORM\|POST-FORMS-COMMIT\|1098 | T005 | OI-20 | Blocked check `LegacyExternalCalls.ValidateTotalInvoice`, called at the same point before the commit; its `NotImplementedException` is caught and the create commits, listing OI-20 (D-45) |
| Trigger\|FORM\|FORM\|WHEN-FORM-NAVIGATE\|1099 | T006 | N | UI mechanics: form navigation state |
| Trigger\|FORM\|FORM\|ON-CLEAR-DETAILS\|1100 | T007 | N | UI mechanics: the API returns header and lines together |
| Trigger\|FORM\|FORM\|WHEN-WINDOW-ACTIVATED\|1101 | T008 | N | UI mechanics: window activation state |
| Trigger\|FORM\|FORM\|PRE-COMMIT\|1102 | T009 | DR-02, PR-14 | `InvoiceDetailRules.RequireDetails`; engine -20903 re-checks (UNVERIFIED) |
| VisualAttribute\|FORM\|DISPLAY\|-\|1103 | — | N, OI-54 | UI mechanics: styling inherited from `BUSINESSXP.olb` |
| VisualAttribute\|FORM\|REQUIRED\|-\|1104 | — | N, OI-54 | UI mechanics: styling inherited from `BUSINESSXP.olb` |
| VisualAttribute\|FORM\|NORMAL\|-\|1105 | — | N, OI-54 | UI mechanics: styling inherited from `BUSINESSXP.olb` |
| VisualAttribute\|FORM\|PROMPT_REQUIRED\|-\|1106 | — | N, OI-54 | UI mechanics: styling inherited from `BUSINESSXP.olb` |
| VisualAttribute\|FORM\|PROMPT_NORMAL\|-\|1107 | — | N, OI-54 | UI mechanics: styling inherited from `BUSINESSXP.olb` |
| VisualAttribute\|FORM\|PROMPT_DISPLAY\|-\|1108 | — | N, OI-54 | UI mechanics: styling inherited from `BUSINESSXP.olb` |
| VisualAttribute\|FORM\|CURRENT_RECORD\|-\|1109 | — | N, OI-54 | UI mechanics: styling inherited from `BUSINESSXP.olb` |
| Window\|FORM\|WINDOW1\|-\|1110 | — | N | UI container, realised as the Web `App` shell |

### §9.2 Reverse half (target → source)

| Key | Source construct(s) |
|---|---|
| Api/Program.cs | Infrastructure: DI and hosting |
| App | T086, T050 canvas swap between `CANVAS2` and `MORE`; window `WINDOW1` |
| Billing.Invoicing.Api.Composition.InvoicingDataRegistration | Infrastructure: DI registration of the Data layer by its ports (D-48, D-55) |
| Billing.Invoicing.Api.Composition.InvoicingDataRegistration.AddInvoicingData | Infrastructure: binds `ConnectionStrings:HisOracle` and `Invoicing:*` into `InvoicingDataOptions` and registers each Data class by its port (D-48) |
| Billing.Invoicing.Api.Context.OperatorContextMiddleware | Forms globals `global.u_no`, `current_info_center_id`, `machine_n` (OI-30), supplied as `X-His-*` headers (D-17) |
| Billing.Invoicing.Api.Context.OperatorContextMiddleware.InvokeAsync | Forms globals (OI-30): `X-His-*` headers → `OperatorContext`; a missing header → 422 (D-17) |
| Billing.Invoicing.Api.Contracts.BundledOfferRequest | T093 (PR-17) |
| Billing.Invoicing.Api.Contracts.CoverageResponse | T023, T026 (DR-03, DR-24; advisory OI-21, OI-23, OI-24) |
| Billing.Invoicing.Api.Contracts.CreateInvoiceRequest | T079 Save (PR-22) |
| Billing.Invoicing.Api.Contracts.CreateInvoiceResponse | T079 Save and `t_full_invoice_result` (PR-22, PR-23; OI-20) |
| Billing.Invoicing.Api.Contracts.DraftDto | `T_INV` / `D_INV` items carried as `InvoiceHeaderDraft` / `InvoiceLineDraft`; request id (PR-22, D-43); T015 |
| Billing.Invoicing.Api.Contracts.ImportRequestsRequest | T085 (DR-18, PR-19) |
| Billing.Invoicing.Api.Contracts.ImportResponse | T085, T089, T093, T029 / T030 and `t_import_result` (PR-17, PR-18, PR-19, PR-21, DR-18, DR-19) |
| Billing.Invoicing.Api.Contracts.InvoiceViewResponse | T010, T012 (DR-17) |
| Billing.Invoicing.Api.Contracts.LastInvoiceNoResponse | T082 `MAX(INV_NO)` of the operator's information centre (OI-15.01) |
| Billing.Invoicing.Api.Contracts.LookupItem | T004 list items from `INVOICES_TYPE`, `CURRENCIES` (OI-15.20, OI-15.21) |
| Billing.Invoicing.Api.Contracts.LovResponse | The 11 used LOV record groups (9 served; `SERVICES` OI-24, `DOC1` OI-33; D-49) |
| Billing.Invoicing.Api.Contracts.MessageDto | PU08 `MESSAG` texts and severities, alert `ERR_ALERT` (DR-01 … DR-25) |
| Billing.Invoicing.Api.Contracts.MoreDetailsResponse | Canvas `MORE`, T012, T056, T075 (OI-15.03, OI-15.27, D-111) |
| Billing.Invoicing.Api.Contracts.NewDraftResponse | T015, T022, T003 (DR-20, DR-24) |
| Billing.Invoicing.Api.Contracts.PackageImportRequest | T089 (PR-18, D-38) |
| Billing.Invoicing.Api.Contracts.PreviewResponse | PU10 `SMALL_CALC` and `t_preview_totals` (PR-01 … PR-08; DR-08, DR-09) |
| Billing.Invoicing.Api.Contracts.ValidateDraftRequest | WHEN-VALIDATE-ITEM / RECORD triggers T014, T023, T026, T029, T031 … T033, T039, T041, T042, T052, T066, T068, T074, T077, T078 |
| Billing.Invoicing.Api.Contracts.ValidateDraftResponse | Validation triggers T014, T023, T029, T031, T066 (DR-01 … DR-25 messages; DR-25 visit line; T023 / T026 coverage for PATIENTNO, D-85; advisory OI-21, OI-22, OI-23, OI-24, OI-33) |
| Billing.Invoicing.Api.Contracts.VisitLineRequest | T029, T030 (DR-25, PR-21) |
| Billing.Invoicing.Api.Controllers.DraftsController | T015, T022, T003; the validation triggers (DR-01 … DR-25) |
| Billing.Invoicing.Api.Controllers.DraftsController.New | T015, T022, T003 (DR-20, DR-24; OI-31) |
| Billing.Invoicing.Api.Controllers.DraftsController.Validate | T014, T023, T026, T029, T031 … T033, T039, T041, T042, T052, T066, T068, T074, T077, T078 |
| Billing.Invoicing.Api.Controllers.ImportsController | T085, T029, T030, T089, T093 (PR-17, PR-18, PR-19, PR-21) |
| Billing.Invoicing.Api.Controllers.ImportsController.BundledOffer | T093 (PR-17) |
| Billing.Invoicing.Api.Controllers.ImportsController.Package | T089 (PR-18, DR-19; OI-24) |
| Billing.Invoicing.Api.Controllers.ImportsController.Requests | T085 (DR-18, PR-19) |
| Billing.Invoicing.Api.Controllers.ImportsController.VisitLine | T029, T030 (DR-25, PR-21) |
| Billing.Invoicing.Api.Controllers.InvoicesController | PU10 `SMALL_CALC`, T079, T010, T082, T086, T081, T080, T094, T095, T013 / T062, T091 |
| Billing.Invoicing.Api.Controllers.InvoicesController.Create | T079, T009, T011, T016, T061 (PR-09 … PR-14, PR-20, PR-22 … PR-25) |
| Billing.Invoicing.Api.Controllers.InvoicesController.Documents | T079 print, T080, T094, T095, PU25 (OI-11, OI-47, OI-48) |
| Billing.Invoicing.Api.Controllers.InvoicesController.Get | T010, T012 (DR-17) |
| Billing.Invoicing.Api.Controllers.InvoicesController.Last | T082 (OI-15.01) |
| Billing.Invoicing.Api.Controllers.InvoicesController.More | Canvas `MORE`, T012, T056, T075 (OI-15.03, D-111) |
| Billing.Invoicing.Api.Controllers.InvoicesController.Preview | PU10 `SMALL_CALC` (PR-01 … PR-08) |
| Billing.Invoicing.Api.Controllers.InvoicesController.Sms | T081 (OI-12, OI-45) |
| Billing.Invoicing.Api.Controllers.InvoicesController.StockTransfer | T091 (OI-10, OI-44) |
| Billing.Invoicing.Api.Controllers.InvoicesController.Update | T013, T017, T057 … T060, T062, T063 (OI-56) |
| Billing.Invoicing.Api.Controllers.LookupsController | The 11 used LOVs (D-49); T004 |
| Billing.Invoicing.Api.Controllers.LookupsController.Currencies | T004 (OI-15.21) |
| Billing.Invoicing.Api.Controllers.LookupsController.InvoiceTypes | T004 (OI-15.20) |
| Billing.Invoicing.Api.Controllers.LookupsController.Lov | The 11 used LOVs: 9 served, `SERVICES` → 501 OI-24, `DOC1` → 501 OI-33 (D-49) |
| Billing.Invoicing.Api.Controllers.PatientsController | T023, T026 (DR-03, DR-24) |
| Billing.Invoicing.Api.Controllers.PatientsController.Coverage | T023, T026 (DR-03, DR-24) |
| Billing.Invoicing.Api.Errors.ModelStateFieldMap | PU08 `MESSAG` / `FORM_TRIGGER_FAILURE`: field of a malformed request value in the error contract |
| Billing.Invoicing.Api.Errors.ModelStateFieldMap.FieldOf | PU08 `MESSAG`: model-state key → the `T_INV` / `D_INV` item or module parameter named by a 422 `field-validation` message |
| Billing.Invoicing.Api.Errors.ProblemDetailsExceptionHandler | PU08 `MESSAG` / `FORM_TRIGGER_FAILURE`, alert `ERR_ALERT`: exceptions answered with the error contract, logged as redacted metadata only (D-102) |
| Billing.Invoicing.Api.Errors.ProblemDetailsExceptionHandler.TryHandleAsync | PU08 `MESSAG` / `FORM_TRIGGER_FAILURE`: exception → `ProblemDetailsWriter` body; logs status, type, Oracle number, package, kind, open item, exception types and route template, never message text (D-102) |
| Billing.Invoicing.Api.Errors.ProblemDetailsWriter | PU08 `MESSAG` / `FORM_TRIGGER_FAILURE`, alert `ERR_ALERT` (error contract) |
| Billing.Invoicing.Api.Errors.ProblemDetailsWriter.WriteAsync | PU08 `MESSAG` / `FORM_TRIGGER_FAILURE`: `DataFailure`, `NotImplementedException` and blocking results → HTTP |
| Billing.Invoicing.Api.Errors.ProblemDetailsWriter.WriteNotFoundAsync | PU08 `MESSAG` (error contract): an unknown invoice (T010), last invoice (T082) or LOV → 404 `not-found` |
| Billing.Invoicing.Api.Services.CreateInvoiceOutcome | T079 Save: the saved or replayed invoice (PR-22), or the blocking pre-flight messages of T014, T009 (DR-01 … DR-24) with the gate ids (OI-23, OI-31, OI-32, OI-33) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService | Trigger order T014 → T009 → T011 → T016 → T061 → T005; T079 |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.BuildDocument | T079 print, T080, T094, T095, PU25 (OI-11, OI-45 … OI-49) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.Create | T079, T014, T009, T011, T016, T061, T005 in commit order (DR-01 … DR-24, PR-09 … PR-25, OI-20) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.GetCoverage | T023, T026 (DR-03, DR-24) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.GetCurrencies | T004 (OI-15.21) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.GetInvoice | T010, T012 (DR-17) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.GetInvoiceTypes | T004 (OI-15.20) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.GetLastInvoiceNo | T082 (OI-15.01) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.GetLov | The 11 used LOVs (D-49; OI-24, OI-33) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.GetMoreDetails | Canvas `MORE`, T012, T056, T075 (OI-15.03, D-111) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.ImportBundledOffer | T093 (PR-17) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.ImportPackage | T089 (PR-18, DR-19, DR-23; OI-24) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.ImportRequests | T085 (DR-18, PR-19) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.ImportVisitLine | T029, T030 (DR-25, PR-21) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.NewDraft | T015, T022, T003 (DR-20, DR-24; OI-31) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.Preview | PU10 `SMALL_CALC` (T058, T060, T065 … T073; PR-01 … PR-08; DR-08, DR-09; OI-23) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.SendSms | T081 (OI-12, OI-45) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.TransferStock | T091 (OI-10, OI-44) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.Update | T013, T017, T057 … T060, T062, T063 (OI-56) |
| Billing.Invoicing.Api.Services.InvoiceWorkflowService.Validate | The validation triggers (DR-01 … DR-25; OI-22, OI-23, OI-31, OI-32, OI-33) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls | T005, T023, T031, T015, T003, T080, T094, T095, T016, T081, T091 (OI-10, OI-12, OI-20 … OI-24, OI-44, OI-45, OI-47, OI-48) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.BuildLegacyDocument | T080, T094, T095 (OI-47, OI-48, OI-26) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.ComputePatientAgeYears | T031 (OI-22) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.GetPaidBefore | T015, T031, PU10 `SMALL_CALC` (OI-23) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.GetPreAuthorization | T023 (OI-21) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.ResolvePricePlan | T003, T023, PU16 `MAKE_CASH` (OI-24) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.SendInvoiceSms | T016, T081 (OI-12, OI-45) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.TransferStock | T091 (OI-10, OI-44) |
| Billing.Invoicing.Data.Blocked.LegacyExternalCalls.ValidateTotalInvoice | T005 (OI-20, D-45) |
| Billing.Invoicing.Data.Blocked.PackageConsumptionGateway | T061, T066, T068, PU19 `OKA` (OI-31) |
| Billing.Invoicing.Data.Blocked.PackageConsumptionGateway.Begin | T061, T066, T068, PU19 `OKA` (OI-31) |
| Billing.Invoicing.Data.Commands.PatientTransferCommand | T011 (DR-21) |
| Billing.Invoicing.Data.Commands.PatientTransferCommand.ClearReceptionTransfer | T011 (DR-21, D-44) |
| Billing.Invoicing.Data.Errors.DataFailure | Infrastructure: error-contract payload (status, kind, number, package, field, legacy text, open item) |
| Billing.Invoicing.Data.Errors.OracleErrorCatalog | Infrastructure: catalogued rows of the §8 register, matched on package, number and prefix (D-24), including T041's legacy text for -20914 |
| Billing.Invoicing.Data.Errors.OracleErrorCatalog.Find | Infrastructure: three-part catalogue lookup (D-24) |
| Billing.Invoicing.Data.Errors.OracleErrorInfo | Infrastructure: Oracle error record copied from `OracleException` |
| Billing.Invoicing.Data.Errors.OracleErrorParser | Infrastructure: ORA stack parsing into number, text and frames |
| Billing.Invoicing.Data.Errors.OracleErrorParser.FromException | Infrastructure: `OracleException` adapter |
| Billing.Invoicing.Data.Errors.OracleErrorParser.FromParts | Infrastructure: builds the Oracle error record from number and message |
| Billing.Invoicing.Data.Errors.OracleFailureTranslator | Infrastructure: exception → HTTP classification of the error contract |
| Billing.Invoicing.Data.Errors.OracleFailureTranslator.Translate | Infrastructure: failure classification of the error contract |
| Billing.Invoicing.Data.Oracle.InvoicingDataOptions | Infrastructure: data-layer settings (D-48) |
| Billing.Invoicing.Data.Oracle.OracleSession | Infrastructure: unit of work replacing the Forms commit transaction (D-10) |
| Billing.Invoicing.Data.Oracle.OracleSession.Commit | Infrastructure: the single .NET-owned commit (D-10) |
| Billing.Invoicing.Data.Oracle.OracleSession.DisposeAsync | Infrastructure: attempted rollback of an uncommitted session, whose outcome can remain uncertain (D-10, D-89) |
| Billing.Invoicing.Data.Oracle.OracleSession.Rollback | Infrastructure: rollback, or rollback to savepoint `dr21` (D-44) |
| Billing.Invoicing.Data.Oracle.OracleSession.Save | Infrastructure: savepoint `dr21` (D-44) |
| Billing.Invoicing.Data.Oracle.OracleSessionFactory | Infrastructure: connection and transaction owner, since the packages never commit (D-10) |
| Billing.Invoicing.Data.Oracle.OracleSessionFactory.Open | Infrastructure: opens a connection and its transaction (D-10) |
| Billing.Invoicing.Data.Plsql.BilImportGateway | T085 (PR-19); T029, T030 (PR-21) |
| Billing.Invoicing.Data.Plsql.BilImportGateway.ApprovalCheckMode | T085 `X422_APPROV_CHECK` (PR-19, D-12) |
| Billing.Invoicing.Data.Plsql.BilImportGateway.GetVisitLine | T029, T030 (PR-21) |
| Billing.Invoicing.Data.Plsql.BilImportGateway.ImportRequestLines | T085 (PR-19) |
| Billing.Invoicing.Data.Plsql.BilInvoiceApiGateway | PU10 `SMALL_CALC`, PU17 `DO_DISC`, PU23 `CHK_ADV_CLASS`, PU19 `OKA`, PU20 `ROUND_FOR_CASH`, T009, T011, T016, T061, T079, T089, T093 |
| Billing.Invoicing.Data.Plsql.BilInvoiceApiGateway.BuildPrintUrl | T079, PU25 (OI-11) |
| Billing.Invoicing.Data.Plsql.BilInvoiceApiGateway.CalculatePreview | PU10 `SMALL_CALC`, PU17 `DO_DISC`, PU23 `CHK_ADV_CLASS`, PU19 `OKA`, PU20 `ROUND_FOR_CASH`; T067, T070, T071 re-preview (PR-01 … PR-08, PR-13 … PR-15, PR-24); T066 standard-offer branch, repeated by T085 on imported request lines (PR-16) |
| Billing.Invoicing.Data.Plsql.BilInvoiceApiGateway.CreateFullInvoice | T009, T011, T016, T061, T079; T003 shift gate as PR-10 (PR-09 … PR-14, PR-20, PR-22 … PR-25) |
| Billing.Invoicing.Data.Plsql.BilInvoiceApiGateway.GetBundledOfferLines | T093 (PR-17) |
| Billing.Invoicing.Data.Plsql.BilInvoiceApiGateway.GetPackageLines | T089 (PR-18, D-38) |
| Billing.Invoicing.Data.Plsql.BilInvoiceApiGateway.SealDraftDate | T015 (`INVDATE := sysdate` at record creation): seal of the draft date issued with the draft, under `Invoicing:DraftSealKey` (D-39) |
| Billing.Invoicing.Data.Plsql.ClientIdBinder | Infrastructure: `t_client_id_tab` binding (D-09) |
| Billing.Invoicing.Data.Plsql.ClientIdBinder.Bind | Infrastructure: `t_client_id_tab` binding (D-09) |
| Billing.Invoicing.Data.Plsql.EditablePreviewLine | Infrastructure: DTO of package record `t_editable_preview_line` |
| Billing.Invoicing.Data.Plsql.EngineLineInput | Infrastructure: DTO of package record `t_line_input` |
| Billing.Invoicing.Data.Plsql.FullInvoiceResultRow | Infrastructure: DTO of the read parts of package record `t_full_invoice_result` |
| Billing.Invoicing.Data.Plsql.HeaderInputBinder | Infrastructure: `T_HEADER_INPUT` scalar binding (D-09, D-41, D-52) |
| Billing.Invoicing.Data.Plsql.HeaderInputBinder.Bind | Infrastructure: `T_HEADER_INPUT` binding (D-09, D-41, D-52) |
| Billing.Invoicing.Data.Plsql.ImportResultRow | Infrastructure: DTO of package record `t_import_result` |
| Billing.Invoicing.Data.Plsql.LineInputBinder | Infrastructure: `T_LINE_INPUT_TAB` associative-array binding (D-09, D-42) |
| Billing.Invoicing.Data.Plsql.LineInputBinder.Bind | Infrastructure: `T_LINE_INPUT_TAB` binding (D-09, D-42) |
| Billing.Invoicing.Data.Plsql.OutputArrayReader | Infrastructure: package output-record conversion (D-09) |
| Billing.Invoicing.Data.Plsql.OutputArrayReader.ReadEngineLines | Infrastructure: OUT arrays → `EngineLineInput` |
| Billing.Invoicing.Data.Plsql.OutputArrayReader.ReadFullInvoiceResult | Infrastructure: OUT scalars → `FullInvoiceResultRow` |
| Billing.Invoicing.Data.Plsql.OutputArrayReader.ReadImportResult | Infrastructure: OUT scalars → `ImportResultRow` |
| Billing.Invoicing.Data.Plsql.OutputArrayReader.ReadPreviewLines | Infrastructure: OUT arrays → `EditablePreviewLine` |
| Billing.Invoicing.Data.Plsql.OutputArrayReader.ReadPreviewTotals | Infrastructure: OUT scalars → `PreviewTotalsRow` |
| Billing.Invoicing.Data.Plsql.PlsqlBlocks | Infrastructure: anonymous PL/SQL block texts, one per use case (D-09) |
| Billing.Invoicing.Data.Plsql.PreviewTotalsRow | Infrastructure: DTO of package record `t_preview_totals` |
| Billing.Invoicing.Data.Ports.IBilImportGateway | Infrastructure: port implemented by `BilImportGateway` (D-55) |
| Billing.Invoicing.Data.Ports.IBilInvoiceApiGateway | Infrastructure: port implemented by `BilInvoiceApiGateway` (D-55) |
| Billing.Invoicing.Data.Ports.IInvoiceQueries | Infrastructure: port implemented by `InvoiceQueries` (D-55) |
| Billing.Invoicing.Data.Ports.ILegacyExternalCalls | Infrastructure: port implemented by `LegacyExternalCalls` (D-55) |
| Billing.Invoicing.Data.Ports.ILookupQueries | Infrastructure: port implemented by `LookupQueries` (D-55) |
| Billing.Invoicing.Data.Ports.ILovQueries | Infrastructure: port implemented by `LovQueries` (D-55) |
| Billing.Invoicing.Data.Ports.IOracleSession | Infrastructure: port implemented by `OracleSession` (D-55) |
| Billing.Invoicing.Data.Ports.IOracleSessionFactory | Infrastructure: port implemented by `OracleSessionFactory` (D-55) |
| Billing.Invoicing.Data.Ports.IPackageConsumptionGateway | Infrastructure: port implemented by `PackageConsumptionGateway` (D-55) |
| Billing.Invoicing.Data.Ports.IPatientTransferCommand | Infrastructure: port implemented by `PatientTransferCommand` (D-55) |
| Billing.Invoicing.Data.Queries.InvoiceQueries | T010, T012, T056, T075, T082, T085, T015; the `CREATE_FULL_INVOICE` request-id read (D-54) |
| Billing.Invoicing.Data.Queries.InvoiceQueries.GetClaimPreload | T015 claim preload (DR-20, D-38) |
| Billing.Invoicing.Data.Queries.InvoiceQueries.GetCreateRequest | The `CREATE_FULL_INVOICE` request-id read (PR-22, OI-15.28, D-54) |
| Billing.Invoicing.Data.Queries.InvoiceQueries.GetInvoice | T010, T012 (DR-17, OI-15.01) |
| Billing.Invoicing.Data.Queries.InvoiceQueries.GetLastInvoiceNo | T082 (OI-15.01) |
| Billing.Invoicing.Data.Queries.InvoiceQueries.GetMoreDetails | T012, T056, T075, canvas `MORE` (OI-15.01, OI-15.03, OI-15.27, D-111) |
| Billing.Invoicing.Data.Queries.InvoiceQueries.GetSelectedRequestRows | T085 cursor (DR-18, OI-15.08, D-26) |
| Billing.Invoicing.Data.Queries.LookupQueries | T003, T004, T015, T023, T026, T027, T031, T039, T041, T052, T066, T068, PU23 `CHK_ADV_CLASS` |
| Billing.Invoicing.Data.Queries.LookupQueries.GetClassAdvancedMode | PU23 `CHK_ADV_CLASS` `DISC_CLASSES.USE_ADVANCED` (OI-23, D-51) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetClinicProfile | T031, T032, T033 (DR-04, DR-05) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetCompanyIsDirect | T066 `COMPANYS.IS_DIRECT` price editability (PR-01, D-42) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetCompanyType | T023, T026 (DR-24) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetCurrencies | T004 (OI-15.21) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetDatabaseTime | T015 `SYSDATE` (DR-20, D-39) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetInvoiceTypes | T004 (OI-15.20) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetPackageComponentFlags | T066, PU19 `OKA` `PACKAGE_DTL` (DR-23, OI-32) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetPatientCardId | T027 `PATIENT.CARD_ID` (OI-32, D-52) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetPatientCoverage | T023 `V_PAT_DATA` (DR-03) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetPreferences | T003 `PREF` (DR-14) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetRequestedServices | T052 `PAT_SERV_REQ` (DR-16) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetServiceProfile | T052, T066, T068 `SERVICES` (DR-12, DR-16, DR-23, OI-32, D-80) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetServiceProfiles | T052, T066, T068 `SERVICES` (DR-12, DR-16, DR-23, OI-32, D-80) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetServiceQueueFlags | T029, T066 `SERVICES.ADD_TO_QUE` (DR-23) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetUserMaxDiscount | T039, T041 `USERS_TABLE.MAX_DISC` (DR-06) |
| Billing.Invoicing.Data.Queries.LookupQueries.GetVisitDoctor | T015, T023 `PAT_VISIT_M` (DR-10, DR-11, DR-20) |
| Billing.Invoicing.Data.Queries.LookupQueries.LockClaimPreload | T015 claim preload `T_INV` first invoice `MAX_DEDUCTABLE` and `CARD_ID` locked in the create transaction (OI-23, OI-32, D-52, D-112) |
| Billing.Invoicing.Data.Queries.LookupQueries.LockClassAdvancedMode | PU23 `CHK_ADV_CLASS` `DISC_CLASSES.USE_ADVANCED` locked in the create transaction (OI-23, D-51, D-112) |
| Billing.Invoicing.Data.Queries.LookupQueries.LockPackageComponentFlags | T066, PU19 `OKA` `PACKAGE_DTL` locked in the create transaction (DR-23, OI-32, D-112) |
| Billing.Invoicing.Data.Queries.LookupQueries.LockPatientCardId | T027 `PATIENT.CARD_ID` locked in the create transaction (OI-32, D-52, D-112) |
| Billing.Invoicing.Data.Queries.LookupQueries.LockPatientMaxDeductable | T023 `V_PAT_DATA.MAX_DEDUCTABLE` base-table rows locked in the create transaction (OI-23, D-51, D-112) |
| Billing.Invoicing.Data.Queries.LookupQueries.LockServiceProfiles | T029, T066 `SERVICES` locked in the create transaction (DR-23, OI-32, D-112) |
| Billing.Invoicing.Data.Queries.LovQueries | LOVs `COMPANY1_2`, `SUB_COMPANY`, `THE_CLASS`, `PAY_TYPE1`, `PAY_TYPE2`, `DOC`, `RESERV_NO`, `OFFERS`, `CAT` and their record groups (OI-15.11 … OI-15.19) |
| Billing.Invoicing.Data.Queries.LovQueries.Cat | LOV `CAT`, record group `CAT` (OI-15.11) |
| Billing.Invoicing.Data.Queries.LovQueries.Company | LOV `COMPANY1_2`, record group `COMPANY` (OI-15.12) |
| Billing.Invoicing.Data.Queries.LovQueries.Doc | LOV `DOC`, record group `DOC` (OI-15.16, OI-15.17) |
| Billing.Invoicing.Data.Queries.LovQueries.Offers | LOV `OFFERS`, record group `OFFERS` (OI-15.19) |
| Billing.Invoicing.Data.Queries.LovQueries.PayTypes | LOVs `PAY_TYPE1`, `PAY_TYPE2`, record group `PAY_TYPE` (OI-15.15), projected to the LOV-mapped `PAY_TYPE_ID` and `PAY_TYPE_NAME` (D-105); T044 payment-type name display |
| Billing.Invoicing.Data.Queries.LovQueries.ReservNo | LOV `RESERV_NO` (T049), record group `RESERV_NO` (OI-15.18), projected to `RESERV_NO`, `THE_TIME` and `PATAINTNO` (D-105); the doctor checked with the predicates of record group `DOC` (OI-15.16, OI-15.17, D-106) |
| Billing.Invoicing.Data.Queries.LovQueries.SubCompany | LOV `SUB_COMPANY`, record group `SUB_COMP` (OI-15.13); the company checked against record group `COMPANY` (OI-15.12, D-106) |
| Billing.Invoicing.Data.Queries.LovQueries.TheClass | LOV `THE_CLASS`, record group `THE_CLASS` (OI-15.14); the sub-company checked against record groups `SUB_COMP` and `COMPANY` (OI-15.13, OI-15.12, D-106) |
| Billing.Invoicing.Domain.Model.ClinicProfile | T031 `CLINICS` / `PATIENT`; T032, T033 `SYS_CAT_TYPE` (DR-04, DR-05) |
| Billing.Invoicing.Domain.Model.DiscountLimitChoice | Alert `DISC_ALERT` buttons (DR-06) |
| Billing.Invoicing.Domain.Model.InvoiceEntryParameters | The 32 module parameters (OI-30) |
| Billing.Invoicing.Domain.Model.InvoiceHeaderDraft | `T_INV` items carried by `T_HEADER_INPUT`, plus the Form-only items the rules read (DR-01, DR-05, DR-06, DR-24; OI-33) |
| Billing.Invoicing.Domain.Model.InvoiceLineDraft | `D_INV` items carried by `T_LINE_INPUT` (DR-12 … DR-16; PR-01, PR-02; OI-33) |
| Billing.Invoicing.Domain.Model.Money | Infrastructure: rounding `round(…,2)` as PL/SQL `ROUND` (D-23) |
| Billing.Invoicing.Domain.Model.Money.Round2 | Infrastructure: `round(…,2)` of T039, T041 as PL/SQL `ROUND` (D-23) |
| Billing.Invoicing.Domain.Model.OpenItemIds | Infrastructure: open-item register of `docs/dependency-open-items.md` |
| Billing.Invoicing.Domain.Model.OperatorContext | Forms globals `global.u_no`, `current_info_center_id`, `machine_n`, `lang` (OI-30) |
| Billing.Invoicing.Domain.Model.PatientCoverageSnapshot | T023 `V_PAT_DATA` (DR-03, OI-15.05) |
| Billing.Invoicing.Domain.Model.PaymentAllocation | `T_INV` `AMOUNT_1` / `AMOUNT_2` / `CASH_PAYED` / `REUND` (DR-07, DR-08) |
| Billing.Invoicing.Domain.Model.PreviewTotals | `BIL_INVOICE_API.t_preview_totals` (PR-07, PR-08) |
| Billing.Invoicing.Domain.Model.RuleResult | Infrastructure: rule outcome, messages and adjusted values |
| Billing.Invoicing.Domain.Model.ServiceProfile | T052, T066, T068 `SERVICES` / `PACKAGE_DTL` (DR-12, DR-16, DR-23, OI-32) |
| Billing.Invoicing.Domain.Model.ValidationMessage | PU08 `MESSAG` text, field and severity |
| Billing.Invoicing.Domain.Model.VisitLineChoice | T029 visit-line branches (DR-25) |
| Billing.Invoicing.Domain.Model.VisitLineChoice.FixedService | T029 service '2000' branch (DR-25) |
| Billing.Invoicing.Domain.Rules.AddToListRule | DR-23 (T029, T066, PU19 `OKA`) |
| Billing.Invoicing.Domain.Rules.AddToListRule.Derive | DR-23 (T029, T066, T089, PU19 `OKA`) |
| Billing.Invoicing.Domain.Rules.ClaimNumberRule | DR-10 (T029) |
| Billing.Invoicing.Domain.Rules.ClaimNumberRule.Build | DR-10 (T029) |
| Billing.Invoicing.Domain.Rules.ClinicSuitabilityRules | DR-04 (T031) |
| Billing.Invoicing.Domain.Rules.ClinicSuitabilityRules.CheckAge | DR-04 age part (T031; OI-22) |
| Billing.Invoicing.Domain.Rules.ClinicSuitabilityRules.CheckSex | DR-04 (T031) |
| Billing.Invoicing.Domain.Rules.DoctorSelectionRules | DR-11 (T029) |
| Billing.Invoicing.Domain.Rules.DoctorSelectionRules.Validate | DR-11 (T029) |
| Billing.Invoicing.Domain.Rules.ErClinicRule | DR-05 (T032, T033) |
| Billing.Invoicing.Domain.Rules.ErClinicRule.Validate | DR-05 (T032, T033) |
| Billing.Invoicing.Domain.Rules.FinalDiscountLimitRule | DR-06 (T039, T041, `DISC_ALERT`) |
| Billing.Invoicing.Domain.Rules.FinalDiscountLimitRule.ApplyChoice | DR-06 (`DISC_ALERT` choice, T039) |
| Billing.Invoicing.Domain.Rules.FinalDiscountLimitRule.Evaluate | DR-06 (T039, T041) |
| Billing.Invoicing.Domain.Rules.HeaderRecordRules | DR-01, DR-22 (T014, T038, T040) |
| Billing.Invoicing.Domain.Rules.HeaderRecordRules.ApplyPaymentTypeDefault | DR-22 (T014, T038, T040) |
| Billing.Invoicing.Domain.Rules.HeaderRecordRules.ValidateRecord | DR-01 (T014) |
| Billing.Invoicing.Domain.Rules.InvoiceDefaultsRule | DR-20 (T015, T022, T003) |
| Billing.Invoicing.Domain.Rules.InvoiceDefaultsRule.Apply | DR-20 (T015, T022, T003) |
| Billing.Invoicing.Domain.Rules.InvoiceDetailRules | DR-02 (T009) |
| Billing.Invoicing.Domain.Rules.InvoiceDetailRules.RequireDetails | DR-02 (T009) |
| Billing.Invoicing.Domain.Rules.LineEntryRules | DR-12 … DR-16 (T068, T077, T078, T074, T066, T052) |
| Billing.Invoicing.Domain.Rules.LineEntryRules.RequireService | DR-15 (T066) |
| Billing.Invoicing.Domain.Rules.LineEntryRules.ValidateApproval | DR-14 (T074) |
| Billing.Invoicing.Domain.Rules.LineEntryRules.ValidateDiscountType | DR-13 (T077, T078; PU17 `DO_DISC` message) |
| Billing.Invoicing.Domain.Rules.LineEntryRules.ValidateQuantity | DR-12 (T068) |
| Billing.Invoicing.Domain.Rules.LineEntryRules.WarnNotRequested | DR-16 (T052) |
| Billing.Invoicing.Domain.Rules.PackageImportRules | DR-19 (T089) |
| Billing.Invoicing.Domain.Rules.PackageImportRules.Evaluate | DR-19 (T089) |
| Billing.Invoicing.Domain.Rules.PatientEligibilityRules | DR-03 (T023) |
| Billing.Invoicing.Domain.Rules.PatientEligibilityRules.Evaluate | DR-03 (T023) |
| Billing.Invoicing.Domain.Rules.PayTypeSelectionRule | DR-24 (T015, T023, T026, PU16 `MAKE_CASH`) |
| Billing.Invoicing.Domain.Rules.PayTypeSelectionRule.Decide | DR-24 (T015, T023, T026, PU16 `MAKE_CASH`) |
| Billing.Invoicing.Domain.Rules.PaymentAllocationRules | DR-07, DR-08, DR-09 (T039, T042, T043, T065, PU30, `CASH_COLLECTED` formula) |
| Billing.Invoicing.Domain.Rules.PaymentAllocationRules.AllocateSecondAmount | DR-07 (T042) |
| Billing.Invoicing.Domain.Rules.PaymentAllocationRules.DefaultFirstAmount | DR-07 (T065) |
| Billing.Invoicing.Domain.Rules.PaymentAllocationRules.Refund | DR-08 (PU30, T043) |
| Billing.Invoicing.Domain.Rules.PaymentAllocationRules.ResetAfterDiscountChange | DR-07 (T039, T041) |
| Billing.Invoicing.Domain.Rules.PaymentAllocationRules.TotalCollected | DR-09 (`CASH_COLLECTED` formula) |
| Billing.Invoicing.Domain.Rules.ReceptionTransferRule | DR-21 (T011) |
| Billing.Invoicing.Domain.Rules.ReceptionTransferRule.ShouldClear | DR-21 (T011, D-44) |
| Billing.Invoicing.Domain.Rules.RequestImportRules | DR-18 (T085) |
| Billing.Invoicing.Domain.Rules.RequestImportRules.Notices | DR-18 per-service notices (T085, D-26) |
| Billing.Invoicing.Domain.Rules.RequestImportRules.RequireDoctor | DR-18 (T085) |
| Billing.Invoicing.Domain.Rules.VisitLineRule | DR-25 (T029) |
| Billing.Invoicing.Domain.Rules.VisitLineRule.Choose | DR-25 (T029) |
| Billing.Invoicing.Domain.Workflow.InvoiceStatePolicy | DR-17 (T010, T020, T021, T062); OI-56 |
| Billing.Invoicing.Domain.Workflow.InvoiceStatePolicy.CanDelete | DR-17 (T020, T021; T019, whose detail delete never runs) |
| Billing.Invoicing.Domain.Workflow.InvoiceStatePolicy.CanEdit | DR-17 (T010, T062); OI-56 |
| Billing.Invoicing.Domain.Workflow.OpenItemGate | OI-32 conditions of PU19 `OKA`, T066, PU23 `CHK_ADV_CLASS`; OI-31; the OI-23 deductible condition (D-51) |
| Billing.Invoicing.Domain.Workflow.OpenItemGate.Evaluate | OI-32, OI-31 and OI-23 conditions (PU19 `OKA`, T066, PU23 `CHK_ADV_CLASS`, T061; D-51, D-52) |
| Web/Program.cs | Infrastructure: static hosting of the React build (D-19) |
| api/client | Infrastructure: HTTP access, 422 / 501 / 503 normalisation of the error contract, success-body contract checks (D-118) and severity normalisation of their message lists (D-119) |
| api/types | Infrastructure: TypeScript shapes of `Api.Contracts` |
| components/ConnectivityBanner | Infrastructure: 503 `oracle-unavailable` presentation of the error contract |
| components/FieldMessage | PU08 `MESSAG`, alert `ERR_ALERT` |
| components/InvoiceHeaderForm | `T_INV` header items; T014, T023, T026, T029, T031 … T033 validation on blur |
| components/InvoiceLinesGrid | `D_INV` rows; T058, T066 … T074 validation on blur |
| components/LovPicker | The 9 served LOVs (D-49); T049 view-only `RESERV_NO` |
| components/OpenItemNotice | Infrastructure: 501 and `openItems` presentation of the open-item contract (D-16) |
| components/PaymentPanel | `T_INV` payment items and `TOOL` buttons (DR-06 … DR-08, DR-22; T039, T041 … T043, T079) |
| components/TotalsPanel | `T_INV` summary items (PR-01 … PR-08; DR-09) |
| main | Infrastructure: React 18 entry point mounting `App` |
| screens/InvoiceScreen | Canvas `CANVAS2`; actions T049, T079 … T082, T085, T089, T093 … T095 |
| screens/MoreDetailsScreen | Canvas `MORE`; T050, T056, T091 (OI-10) |
| state/invoiceDraft | Infrastructure: draft state shared by both screens, as `CANVAS2` and `MORE` share the `T_INV` / `D_INV` records |

## §10 Legacy anomalies

Recorded, not fixed; the Form export and the packages stay unchanged.

- **`ROUND_FOR_CASH` credit branch** uses `nvl(VAT_TOTAL_PAT,2)` and does not round [05_Complex/Inv_Small_Cash.xml:979]; the package's `cash_collected` governs (D-40).
- **`CAN_GO_ITEM`** compares against `'TEUE'`, so it always returns FALSE; it is dead code anyway [05_Complex/Inv_Small_Cash.xml:986].
- **T029 `SHOW_LOV('docid')`** names an LOV that does not exist; the error is swallowed [05_Complex/Inv_Small_Cash.xml:43].
- **Swallowed raises:** T029 [05_Complex/Inv_Small_Cash.xml:43], T031 [05_Complex/Inv_Small_Cash.xml:47] and the tail of T085 [05_Complex/Inv_Small_Cash.xml:755] raise inside `when others then null`, so their messages act as warnings (D-27). The T011 shift re-check 'Cant Create Invocie no opened shift for the user and the user is not invoice admin' is swallowed the same way [05_Complex/Inv_Small_Cash.xml:363]; the shift rule itself is PR-10.
- **Dead company-type branches:** T026 tests `V_type=1`, but `V_type` is never assigned, so only "company 0 → cash, otherwise credit" is live [05_Complex/Inv_Small_Cash.xml:27] (DR-24).
- **Unused parameter:** `PARAMETER.LESS_PAYMENT_970` is filled in T003 and never read [05_Complex/Inv_Small_Cash.xml:844, 1096].
- **Commented-out messages:** T044's 'Please select payment type' and 'You can select uncollected with credit invoice and consultaion only' sit inside comments, so they are not rules [05_Complex/Inv_Small_Cash.xml:87] (D-27).
- **PFLAG at noon:** T015 / T025 yield AM at 12:00:00 – 12:00:59; the engine yields PM [05_Complex/Inv_Small_Cash.xml:24, 367], [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:455-467] (D-06, UNVERIFIED).
- **Final-discount percent base:** T039 uses `PAT_PAYX`; the engine uses total net [05_Complex/Inv_Small_Cash.xml:74], [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:1234-1262] (D-07, UNVERIFIED).
- **Tendered cash:** the engine stores `cash_payed = cash_collected` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:2766] (UNVERIFIED); the Form's tendered `CASH_PAYED` and `REUND` are not persisted (OI-33).
- **-20904 skipped on ordinary lines:** `assert_lines_valid` raises -20904 only when `trim_to_null(serviceid) is null and not (offer_type = 0 and upper_trim_to_null(offer_line_role) = 'PARENT')` [05_Complex/APEX_Reference/backend/BIL_INVOICE_ENGINE.sql:557-563] (UNVERIFIED). On an ordinary line `offer_type` and `offer_line_role` are both null, so the parenthesised test is NULL, `not` NULL is NULL and the line is not rejected (UNVERIFIED). It continues into `calculate_lines` and the missing `bil_service_context` (OI-04), so its outcome is UNVERIFIED. -20904 is reachable only when `offer_type` is set and not 0, or `offer_line_role` is set and not 'PARENT', as in the PR-14 fixture's service-missing case (`offerType` 1, `offerLineRole` 'SERVICE'; D-126) (UNVERIFIED). The package stays unchanged; the target's guard is DR-15 'You Must Select Value' (T066, `LineEntryRules.RequireService`), run on line validation and in the create pre-flight (D-04).
