namespace Billing.Invoicing.Data.Plsql;

/// <summary>Anonymous PL/SQL blocks that call BIL_INVOICE_API and BIL_IMPORT through scalar binds and scalar associative arrays. UNVERIFIED against Oracle.</summary>
public static class PlsqlBlocks
{
    /// <summary>Calls BIL_INVOICE_API.EXPAND_BUNDLED_OFFER_IG_LINES, then CALCULATE_EDITABLE_INVOICE_PREVIEW; returns preview lines and totals; copies no line when the preview exceeds max_output_lines, while pl_count reports the full count.</summary>
    public const string Preview = """
        declare
            type t_num is table of number index by pls_integer;
            type t_vc is table of varchar2(4000) index by pls_integer;
            type t_dt is table of date index by pls_integer;
            procedure run(
                h_patientno in varchar2,
                h_invdate in date,
                h_invtypeid in number,
                h_paytype in number,
                h_sub_paytype in number,
                h_sub_paytype2 in number,
                h_clinicid in number,
                h_docid in number,
                h_curr_code in varchar2,
                h_pre_authorization in varchar2,
                h_claim_no in varchar2,
                h_claim_flag in varchar2,
                h_note_no in varchar2,
                h_finaldisc_perc in number,
                h_finaldisc in number,
                h_amount_1 in number,
                h_amount_2 in number,
                h_add_to_list in number,
                h_user_no in number,
                h_machine_n in varchar2,
                h_info_center_id in varchar2,
                l_serviceid in t_vc,
                l_qty in t_num,
                l_price_override in t_num,
                l_use_price_override in t_vc,
                l_discount_type in t_vc,
                l_disc in t_num,
                l_my_disc in t_num,
                l_teeth_no in t_vc,
                l_tooth_surface in t_vc,
                l_teeth_no2 in t_vc,
                l_pat_serv_req_row_id in t_num,
                l_approv_date in t_dt,
                l_approv_validity in t_num,
                l_approv_ref_no in t_vc,
                l_claim_no in t_vc,
                l_req_need_a in t_num,
                l_req_a_status in t_num,
                l_package_service_id in t_vc,
                l_package_instance_id in t_vc,
                l_package_line_role in t_vc,
                l_package_component_order in t_num,
                l_package_parent_line_id in t_num,
                l_package_pricing_method in t_vc,
                l_package_definition_token in t_vc,
                l_offer_id in t_num,
                l_offer_dtl_id in t_num,
                l_offer_type in t_num,
                l_offer_instance_id in t_vc,
                l_offer_line_role in t_vc,
                l_offer_parent_line_id in t_num,
                l_offer_price_applied in t_num,
                l_offer_dis_applied in t_num,
                l_offer_name_snapshot in t_vc,
                l_offer_object_version_number in t_num,
                l_offer_dtl_object_version_number in t_num,
                line_count in number,
                l_client_id in t_vc,
                amount_1 in number,
                amount_2 in number,
                amount_1_auto in varchar2,
                max_output_lines in pls_integer,
                pl_client_id out t_vc,
                pl_line_no out t_num,
                pl_serviceid out t_vc,
                pl_servicedesc out t_vc,
                pl_catid out t_num,
                pl_list_id out t_num,
                pl_curr_code out t_vc,
                pl_qty out t_num,
                pl_price out t_num,
                pl_plan_discount_pct out t_num,
                pl_plan_discount_amount out t_num,
                pl_manual_discount_type out t_vc,
                pl_manual_discount_pct out t_num,
                pl_manual_discount_amount out t_num,
                pl_discount_source out t_vc,
                pl_disc out t_num,
                pl_my_disc out t_num,
                pl_my_price out t_num,
                pl_my_net out t_num,
                pl_the_pay out t_num,
                pl_the_comp out t_num,
                pl_vat_rate out t_num,
                pl_vat_val_pat out t_num,
                pl_vat_val_co out t_num,
                pl_vat_val_pat_ex out t_num,
                pl_req_need_a out t_num,
                pl_req_a_status out t_num,
                pl_allow_manual_discount out t_vc,
                pl_allow_price_override out t_vc,
                pl_package_service_id out t_vc,
                pl_package_instance_id out t_vc,
                pl_package_line_role out t_vc,
                pl_package_component_order out t_num,
                pl_package_parent_line_id out t_num,
                pl_package_pricing_method out t_vc,
                pl_package_definition_token out t_vc,
                pl_offer_id out t_num,
                pl_offer_dtl_id out t_num,
                pl_offer_type out t_num,
                pl_offer_instance_id out t_vc,
                pl_offer_line_role out t_vc,
                pl_offer_parent_line_id out t_num,
                pl_offer_price_applied out t_num,
                pl_offer_dis_applied out t_num,
                pl_offer_name_snapshot out t_vc,
                pl_offer_object_version_number out t_num,
                pl_offer_dtl_object_version_number out t_num,
                pl_count out number,
                pt_line_count out number,
                pt_total_gross out number,
                pt_total_discount out number,
                pt_total_net out number,
                pt_pat_pay out number,
                pt_comp_pay out number,
                pt_vat_total_pat out number,
                pt_vat_total_co out number,
                pt_cash_collected out number,
                pt_amount_1 out number,
                pt_amount_2 out number,
                pt_remaining_amount out number,
                pt_payment_status out varchar2
            ) is
                v_header bil_invoice_engine.t_header_input;
                v_lines bil_invoice_engine.t_line_input_tab;
                v_client_ids bil_invoice_api.t_client_id_tab;
                v_full_lines bil_invoice_engine.t_line_input_tab;
                v_full_client_ids bil_invoice_api.t_client_id_tab;
                v_preview bil_invoice_api.t_editable_preview_line_tab;
                v_totals bil_invoice_api.t_preview_totals;
                v_idx pls_integer;
                v_out pls_integer := 0;
            begin
                v_header.patientno := h_patientno;
                v_header.invdate := h_invdate;
                v_header.invtypeid := h_invtypeid;
                v_header.paytype := h_paytype;
                v_header.sub_paytype := h_sub_paytype;
                v_header.sub_paytype2 := h_sub_paytype2;
                v_header.clinicid := h_clinicid;
                v_header.docid := h_docid;
                v_header.curr_code := h_curr_code;
                v_header.pre_authorization := h_pre_authorization;
                v_header.claim_no := h_claim_no;
                v_header.claim_flag := h_claim_flag;
                v_header.note_no := h_note_no;
                v_header.finaldisc_perc := h_finaldisc_perc;
                v_header.finaldisc := h_finaldisc;
                v_header.amount_1 := h_amount_1;
                v_header.amount_2 := h_amount_2;
                v_header.add_to_list := h_add_to_list;
                v_header.user_no := h_user_no;
                v_header.machine_n := h_machine_n;
                v_header.info_center_id := h_info_center_id;

                for i in 1 .. line_count loop
                    v_lines(i).serviceid := l_serviceid(i);
                    v_lines(i).qty := l_qty(i);
                    v_lines(i).price_override := l_price_override(i);
                    v_lines(i).use_price_override := l_use_price_override(i);
                    v_lines(i).discount_type := l_discount_type(i);
                    v_lines(i).disc := l_disc(i);
                    v_lines(i).my_disc := l_my_disc(i);
                    v_lines(i).teeth_no := l_teeth_no(i);
                    v_lines(i).tooth_surface := l_tooth_surface(i);
                    v_lines(i).teeth_no2 := l_teeth_no2(i);
                    v_lines(i).pat_serv_req_row_id := l_pat_serv_req_row_id(i);
                    v_lines(i).approv_date := l_approv_date(i);
                    v_lines(i).approv_validity := l_approv_validity(i);
                    v_lines(i).approv_ref_no := l_approv_ref_no(i);
                    v_lines(i).claim_no := l_claim_no(i);
                    v_lines(i).req_need_a := l_req_need_a(i);
                    v_lines(i).req_a_status := l_req_a_status(i);
                    v_lines(i).package_service_id := l_package_service_id(i);
                    v_lines(i).package_instance_id := l_package_instance_id(i);
                    v_lines(i).package_line_role := l_package_line_role(i);
                    v_lines(i).package_component_order := l_package_component_order(i);
                    v_lines(i).package_parent_line_id := l_package_parent_line_id(i);
                    v_lines(i).package_pricing_method := l_package_pricing_method(i);
                    v_lines(i).package_definition_token := l_package_definition_token(i);
                    v_lines(i).offer_id := l_offer_id(i);
                    v_lines(i).offer_dtl_id := l_offer_dtl_id(i);
                    v_lines(i).offer_type := l_offer_type(i);
                    v_lines(i).offer_instance_id := l_offer_instance_id(i);
                    v_lines(i).offer_line_role := l_offer_line_role(i);
                    v_lines(i).offer_parent_line_id := l_offer_parent_line_id(i);
                    v_lines(i).offer_price_applied := l_offer_price_applied(i);
                    v_lines(i).offer_dis_applied := l_offer_dis_applied(i);
                    v_lines(i).offer_name_snapshot := l_offer_name_snapshot(i);
                    v_lines(i).offer_object_version_number := l_offer_object_version_number(i);
                    v_lines(i).offer_dtl_object_version_number := l_offer_dtl_object_version_number(i);
                    v_client_ids(i) := l_client_id(i);
                end loop;

                bil_invoice_api.expand_bundled_offer_ig_lines(
                    p_visible_lines => v_lines,
                    p_visible_client_ids => v_client_ids,
                    o_full_lines => v_full_lines,
                    o_full_client_ids => v_full_client_ids
                );

                bil_invoice_api.calculate_editable_invoice_preview(
                    p_header => v_header,
                    p_lines => v_full_lines,
                    p_client_ids => v_full_client_ids,
                    p_amount_1 => amount_1,
                    p_amount_2 => amount_2,
                    p_amount_1_auto => amount_1_auto,
                    o_lines => v_preview,
                    o_totals => v_totals
                );

                if v_preview.count > max_output_lines then
                    v_idx := null;
                else
                    v_idx := v_preview.first;
                end if;
                while v_idx is not null loop
                    v_out := v_out + 1;
                    pl_client_id(v_out) := v_preview(v_idx).client_id;
                    pl_line_no(v_out) := v_preview(v_idx).line_no;
                    pl_serviceid(v_out) := v_preview(v_idx).serviceid;
                    pl_servicedesc(v_out) := v_preview(v_idx).servicedesc;
                    pl_catid(v_out) := v_preview(v_idx).catid;
                    pl_list_id(v_out) := v_preview(v_idx).list_id;
                    pl_curr_code(v_out) := v_preview(v_idx).curr_code;
                    pl_qty(v_out) := v_preview(v_idx).qty;
                    pl_price(v_out) := v_preview(v_idx).price;
                    pl_plan_discount_pct(v_out) := v_preview(v_idx).plan_discount_pct;
                    pl_plan_discount_amount(v_out) := v_preview(v_idx).plan_discount_amount;
                    pl_manual_discount_type(v_out) := v_preview(v_idx).manual_discount_type;
                    pl_manual_discount_pct(v_out) := v_preview(v_idx).manual_discount_pct;
                    pl_manual_discount_amount(v_out) := v_preview(v_idx).manual_discount_amount;
                    pl_discount_source(v_out) := v_preview(v_idx).discount_source;
                    pl_disc(v_out) := v_preview(v_idx).disc;
                    pl_my_disc(v_out) := v_preview(v_idx).my_disc;
                    pl_my_price(v_out) := v_preview(v_idx).my_price;
                    pl_my_net(v_out) := v_preview(v_idx).my_net;
                    pl_the_pay(v_out) := v_preview(v_idx).the_pay;
                    pl_the_comp(v_out) := v_preview(v_idx).the_comp;
                    pl_vat_rate(v_out) := v_preview(v_idx).vat_rate;
                    pl_vat_val_pat(v_out) := v_preview(v_idx).vat_val_pat;
                    pl_vat_val_co(v_out) := v_preview(v_idx).vat_val_co;
                    pl_vat_val_pat_ex(v_out) := v_preview(v_idx).vat_val_pat_ex;
                    pl_req_need_a(v_out) := v_preview(v_idx).req_need_a;
                    pl_req_a_status(v_out) := v_preview(v_idx).req_a_status;
                    pl_allow_manual_discount(v_out) := v_preview(v_idx).allow_manual_discount;
                    pl_allow_price_override(v_out) := v_preview(v_idx).allow_price_override;
                    pl_package_service_id(v_out) := v_preview(v_idx).package_service_id;
                    pl_package_instance_id(v_out) := v_preview(v_idx).package_instance_id;
                    pl_package_line_role(v_out) := v_preview(v_idx).package_line_role;
                    pl_package_component_order(v_out) := v_preview(v_idx).package_component_order;
                    pl_package_parent_line_id(v_out) := v_preview(v_idx).package_parent_line_id;
                    pl_package_pricing_method(v_out) := v_preview(v_idx).package_pricing_method;
                    pl_package_definition_token(v_out) := v_preview(v_idx).package_definition_token;
                    pl_offer_id(v_out) := v_preview(v_idx).offer_id;
                    pl_offer_dtl_id(v_out) := v_preview(v_idx).offer_dtl_id;
                    pl_offer_type(v_out) := v_preview(v_idx).offer_type;
                    pl_offer_instance_id(v_out) := v_preview(v_idx).offer_instance_id;
                    pl_offer_line_role(v_out) := v_preview(v_idx).offer_line_role;
                    pl_offer_parent_line_id(v_out) := v_preview(v_idx).offer_parent_line_id;
                    pl_offer_price_applied(v_out) := v_preview(v_idx).offer_price_applied;
                    pl_offer_dis_applied(v_out) := v_preview(v_idx).offer_dis_applied;
                    pl_offer_name_snapshot(v_out) := v_preview(v_idx).offer_name_snapshot;
                    pl_offer_object_version_number(v_out) := v_preview(v_idx).offer_object_version_number;
                    pl_offer_dtl_object_version_number(v_out) := v_preview(v_idx).offer_dtl_object_version_number;
                    v_idx := v_preview.next(v_idx);
                end loop;
                pl_count := v_preview.count;
                if v_out = 0 then
                    pl_client_id(1) := null;
                    pl_line_no(1) := null;
                    pl_serviceid(1) := null;
                    pl_servicedesc(1) := null;
                    pl_catid(1) := null;
                    pl_list_id(1) := null;
                    pl_curr_code(1) := null;
                    pl_qty(1) := null;
                    pl_price(1) := null;
                    pl_plan_discount_pct(1) := null;
                    pl_plan_discount_amount(1) := null;
                    pl_manual_discount_type(1) := null;
                    pl_manual_discount_pct(1) := null;
                    pl_manual_discount_amount(1) := null;
                    pl_discount_source(1) := null;
                    pl_disc(1) := null;
                    pl_my_disc(1) := null;
                    pl_my_price(1) := null;
                    pl_my_net(1) := null;
                    pl_the_pay(1) := null;
                    pl_the_comp(1) := null;
                    pl_vat_rate(1) := null;
                    pl_vat_val_pat(1) := null;
                    pl_vat_val_co(1) := null;
                    pl_vat_val_pat_ex(1) := null;
                    pl_req_need_a(1) := null;
                    pl_req_a_status(1) := null;
                    pl_allow_manual_discount(1) := null;
                    pl_allow_price_override(1) := null;
                    pl_package_service_id(1) := null;
                    pl_package_instance_id(1) := null;
                    pl_package_line_role(1) := null;
                    pl_package_component_order(1) := null;
                    pl_package_parent_line_id(1) := null;
                    pl_package_pricing_method(1) := null;
                    pl_package_definition_token(1) := null;
                    pl_offer_id(1) := null;
                    pl_offer_dtl_id(1) := null;
                    pl_offer_type(1) := null;
                    pl_offer_instance_id(1) := null;
                    pl_offer_line_role(1) := null;
                    pl_offer_parent_line_id(1) := null;
                    pl_offer_price_applied(1) := null;
                    pl_offer_dis_applied(1) := null;
                    pl_offer_name_snapshot(1) := null;
                    pl_offer_object_version_number(1) := null;
                    pl_offer_dtl_object_version_number(1) := null;
                end if;

                pt_line_count := v_totals.line_count;
                pt_total_gross := v_totals.total_gross;
                pt_total_discount := v_totals.total_discount;
                pt_total_net := v_totals.total_net;
                pt_pat_pay := v_totals.pat_pay;
                pt_comp_pay := v_totals.comp_pay;
                pt_vat_total_pat := v_totals.vat_total_pat;
                pt_vat_total_co := v_totals.vat_total_co;
                pt_cash_collected := v_totals.cash_collected;
                pt_amount_1 := v_totals.amount_1;
                pt_amount_2 := v_totals.amount_2;
                pt_remaining_amount := v_totals.remaining_amount;
                pt_payment_status := v_totals.payment_status;
            end run;
        begin
            run(
                h_patientno => :h_patientno,
                h_invdate => :h_invdate,
                h_invtypeid => :h_invtypeid,
                h_paytype => :h_paytype,
                h_sub_paytype => :h_sub_paytype,
                h_sub_paytype2 => :h_sub_paytype2,
                h_clinicid => :h_clinicid,
                h_docid => :h_docid,
                h_curr_code => :h_curr_code,
                h_pre_authorization => :h_pre_authorization,
                h_claim_no => :h_claim_no,
                h_claim_flag => :h_claim_flag,
                h_note_no => :h_note_no,
                h_finaldisc_perc => :h_finaldisc_perc,
                h_finaldisc => :h_finaldisc,
                h_amount_1 => :h_amount_1,
                h_amount_2 => :h_amount_2,
                h_add_to_list => :h_add_to_list,
                h_user_no => :h_user_no,
                h_machine_n => :h_machine_n,
                h_info_center_id => :h_info_center_id,
                l_serviceid => :l_serviceid,
                l_qty => :l_qty,
                l_price_override => :l_price_override,
                l_use_price_override => :l_use_price_override,
                l_discount_type => :l_discount_type,
                l_disc => :l_disc,
                l_my_disc => :l_my_disc,
                l_teeth_no => :l_teeth_no,
                l_tooth_surface => :l_tooth_surface,
                l_teeth_no2 => :l_teeth_no2,
                l_pat_serv_req_row_id => :l_pat_serv_req_row_id,
                l_approv_date => :l_approv_date,
                l_approv_validity => :l_approv_validity,
                l_approv_ref_no => :l_approv_ref_no,
                l_claim_no => :l_claim_no,
                l_req_need_a => :l_req_need_a,
                l_req_a_status => :l_req_a_status,
                l_package_service_id => :l_package_service_id,
                l_package_instance_id => :l_package_instance_id,
                l_package_line_role => :l_package_line_role,
                l_package_component_order => :l_package_component_order,
                l_package_parent_line_id => :l_package_parent_line_id,
                l_package_pricing_method => :l_package_pricing_method,
                l_package_definition_token => :l_package_definition_token,
                l_offer_id => :l_offer_id,
                l_offer_dtl_id => :l_offer_dtl_id,
                l_offer_type => :l_offer_type,
                l_offer_instance_id => :l_offer_instance_id,
                l_offer_line_role => :l_offer_line_role,
                l_offer_parent_line_id => :l_offer_parent_line_id,
                l_offer_price_applied => :l_offer_price_applied,
                l_offer_dis_applied => :l_offer_dis_applied,
                l_offer_name_snapshot => :l_offer_name_snapshot,
                l_offer_object_version_number => :l_offer_object_version_number,
                l_offer_dtl_object_version_number => :l_offer_dtl_object_version_number,
                line_count => :line_count,
                l_client_id => :l_client_id,
                amount_1 => :amount_1,
                amount_2 => :amount_2,
                amount_1_auto => :amount_1_auto,
                max_output_lines => :max_output_lines,
                pl_client_id => :pl_client_id,
                pl_line_no => :pl_line_no,
                pl_serviceid => :pl_serviceid,
                pl_servicedesc => :pl_servicedesc,
                pl_catid => :pl_catid,
                pl_list_id => :pl_list_id,
                pl_curr_code => :pl_curr_code,
                pl_qty => :pl_qty,
                pl_price => :pl_price,
                pl_plan_discount_pct => :pl_plan_discount_pct,
                pl_plan_discount_amount => :pl_plan_discount_amount,
                pl_manual_discount_type => :pl_manual_discount_type,
                pl_manual_discount_pct => :pl_manual_discount_pct,
                pl_manual_discount_amount => :pl_manual_discount_amount,
                pl_discount_source => :pl_discount_source,
                pl_disc => :pl_disc,
                pl_my_disc => :pl_my_disc,
                pl_my_price => :pl_my_price,
                pl_my_net => :pl_my_net,
                pl_the_pay => :pl_the_pay,
                pl_the_comp => :pl_the_comp,
                pl_vat_rate => :pl_vat_rate,
                pl_vat_val_pat => :pl_vat_val_pat,
                pl_vat_val_co => :pl_vat_val_co,
                pl_vat_val_pat_ex => :pl_vat_val_pat_ex,
                pl_req_need_a => :pl_req_need_a,
                pl_req_a_status => :pl_req_a_status,
                pl_allow_manual_discount => :pl_allow_manual_discount,
                pl_allow_price_override => :pl_allow_price_override,
                pl_package_service_id => :pl_package_service_id,
                pl_package_instance_id => :pl_package_instance_id,
                pl_package_line_role => :pl_package_line_role,
                pl_package_component_order => :pl_package_component_order,
                pl_package_parent_line_id => :pl_package_parent_line_id,
                pl_package_pricing_method => :pl_package_pricing_method,
                pl_package_definition_token => :pl_package_definition_token,
                pl_offer_id => :pl_offer_id,
                pl_offer_dtl_id => :pl_offer_dtl_id,
                pl_offer_type => :pl_offer_type,
                pl_offer_instance_id => :pl_offer_instance_id,
                pl_offer_line_role => :pl_offer_line_role,
                pl_offer_parent_line_id => :pl_offer_parent_line_id,
                pl_offer_price_applied => :pl_offer_price_applied,
                pl_offer_dis_applied => :pl_offer_dis_applied,
                pl_offer_name_snapshot => :pl_offer_name_snapshot,
                pl_offer_object_version_number => :pl_offer_object_version_number,
                pl_offer_dtl_object_version_number => :pl_offer_dtl_object_version_number,
                pl_count => :pl_count,
                pt_line_count => :pt_line_count,
                pt_total_gross => :pt_total_gross,
                pt_total_discount => :pt_total_discount,
                pt_total_net => :pt_total_net,
                pt_pat_pay => :pt_pat_pay,
                pt_comp_pay => :pt_comp_pay,
                pt_vat_total_pat => :pt_vat_total_pat,
                pt_vat_total_co => :pt_vat_total_co,
                pt_cash_collected => :pt_cash_collected,
                pt_amount_1 => :pt_amount_1,
                pt_amount_2 => :pt_amount_2,
                pt_remaining_amount => :pt_remaining_amount,
                pt_payment_status => :pt_payment_status
            );
        end;
        """;

    /// <summary>Calls BIL_INVOICE_API.EXPAND_BUNDLED_OFFER_IG_LINES when lines are bound, then CREATE_FULL_INVOICE; returns the full invoice result.</summary>
    public const string Create = """
        declare
            type t_num is table of number index by pls_integer;
            type t_vc is table of varchar2(4000) index by pls_integer;
            type t_dt is table of date index by pls_integer;
            procedure run(
                h_patientno in varchar2,
                h_invdate in date,
                h_invtypeid in number,
                h_paytype in number,
                h_sub_paytype in number,
                h_sub_paytype2 in number,
                h_clinicid in number,
                h_docid in number,
                h_curr_code in varchar2,
                h_pre_authorization in varchar2,
                h_claim_no in varchar2,
                h_claim_flag in varchar2,
                h_note_no in varchar2,
                h_finaldisc_perc in number,
                h_finaldisc in number,
                h_amount_1 in number,
                h_amount_2 in number,
                h_add_to_list in number,
                h_user_no in number,
                h_machine_n in varchar2,
                h_info_center_id in varchar2,
                l_serviceid in t_vc,
                l_qty in t_num,
                l_price_override in t_num,
                l_use_price_override in t_vc,
                l_discount_type in t_vc,
                l_disc in t_num,
                l_my_disc in t_num,
                l_teeth_no in t_vc,
                l_tooth_surface in t_vc,
                l_teeth_no2 in t_vc,
                l_pat_serv_req_row_id in t_num,
                l_approv_date in t_dt,
                l_approv_validity in t_num,
                l_approv_ref_no in t_vc,
                l_claim_no in t_vc,
                l_req_need_a in t_num,
                l_req_a_status in t_num,
                l_package_service_id in t_vc,
                l_package_instance_id in t_vc,
                l_package_line_role in t_vc,
                l_package_component_order in t_num,
                l_package_parent_line_id in t_num,
                l_package_pricing_method in t_vc,
                l_package_definition_token in t_vc,
                l_offer_id in t_num,
                l_offer_dtl_id in t_num,
                l_offer_type in t_num,
                l_offer_instance_id in t_vc,
                l_offer_line_role in t_vc,
                l_offer_parent_line_id in t_num,
                l_offer_price_applied in t_num,
                l_offer_dis_applied in t_num,
                l_offer_name_snapshot in t_vc,
                l_offer_object_version_number in t_num,
                l_offer_dtl_object_version_number in t_num,
                line_count in number,
                l_client_id in t_vc,
                amount_1 in number,
                amount_2 in number,
                sub_paytype in number,
                sub_paytype2 in number,
                request_id in varchar2,
                fr_inv_no out number,
                fr_invdate out date,
                fr_patientno out varchar2,
                fr_curr_code out varchar2,
                fr_line_count out number,
                fr_total_gross out number,
                fr_total_discount out number,
                fr_total_net out number,
                fr_pat_pay out number,
                fr_comp_pay out number,
                fr_vat_total_pat out number,
                fr_vat_total_co out number,
                fr_vat_total out number,
                fr_finaldisc out number,
                fr_cash_collected out number,
                fr_shift_system_unique out number,
                fr_payment_posted out varchar2,
                fr_queue_posted out varchar2,
                fr_stock_posted out varchar2,
                fr_print_url_built out varchar2,
                fr_sms_sent out varchar2,
                fr_message out varchar2,
                fr_message_send_status out varchar2,
                fr_message_text out varchar2
            ) is
                v_header bil_invoice_engine.t_header_input;
                v_lines bil_invoice_engine.t_line_input_tab;
                v_client_ids bil_invoice_api.t_client_id_tab;
                v_full_lines bil_invoice_engine.t_line_input_tab;
                v_full_client_ids bil_invoice_api.t_client_id_tab;
                v_result bil_invoice_api.t_full_invoice_result;
            begin
                v_header.patientno := h_patientno;
                v_header.invdate := h_invdate;
                v_header.invtypeid := h_invtypeid;
                v_header.paytype := h_paytype;
                v_header.sub_paytype := h_sub_paytype;
                v_header.sub_paytype2 := h_sub_paytype2;
                v_header.clinicid := h_clinicid;
                v_header.docid := h_docid;
                v_header.curr_code := h_curr_code;
                v_header.pre_authorization := h_pre_authorization;
                v_header.claim_no := h_claim_no;
                v_header.claim_flag := h_claim_flag;
                v_header.note_no := h_note_no;
                v_header.finaldisc_perc := h_finaldisc_perc;
                v_header.finaldisc := h_finaldisc;
                v_header.amount_1 := h_amount_1;
                v_header.amount_2 := h_amount_2;
                v_header.add_to_list := h_add_to_list;
                v_header.user_no := h_user_no;
                v_header.machine_n := h_machine_n;
                v_header.info_center_id := h_info_center_id;

                for i in 1 .. line_count loop
                    v_lines(i).serviceid := l_serviceid(i);
                    v_lines(i).qty := l_qty(i);
                    v_lines(i).price_override := l_price_override(i);
                    v_lines(i).use_price_override := l_use_price_override(i);
                    v_lines(i).discount_type := l_discount_type(i);
                    v_lines(i).disc := l_disc(i);
                    v_lines(i).my_disc := l_my_disc(i);
                    v_lines(i).teeth_no := l_teeth_no(i);
                    v_lines(i).tooth_surface := l_tooth_surface(i);
                    v_lines(i).teeth_no2 := l_teeth_no2(i);
                    v_lines(i).pat_serv_req_row_id := l_pat_serv_req_row_id(i);
                    v_lines(i).approv_date := l_approv_date(i);
                    v_lines(i).approv_validity := l_approv_validity(i);
                    v_lines(i).approv_ref_no := l_approv_ref_no(i);
                    v_lines(i).claim_no := l_claim_no(i);
                    v_lines(i).req_need_a := l_req_need_a(i);
                    v_lines(i).req_a_status := l_req_a_status(i);
                    v_lines(i).package_service_id := l_package_service_id(i);
                    v_lines(i).package_instance_id := l_package_instance_id(i);
                    v_lines(i).package_line_role := l_package_line_role(i);
                    v_lines(i).package_component_order := l_package_component_order(i);
                    v_lines(i).package_parent_line_id := l_package_parent_line_id(i);
                    v_lines(i).package_pricing_method := l_package_pricing_method(i);
                    v_lines(i).package_definition_token := l_package_definition_token(i);
                    v_lines(i).offer_id := l_offer_id(i);
                    v_lines(i).offer_dtl_id := l_offer_dtl_id(i);
                    v_lines(i).offer_type := l_offer_type(i);
                    v_lines(i).offer_instance_id := l_offer_instance_id(i);
                    v_lines(i).offer_line_role := l_offer_line_role(i);
                    v_lines(i).offer_parent_line_id := l_offer_parent_line_id(i);
                    v_lines(i).offer_price_applied := l_offer_price_applied(i);
                    v_lines(i).offer_dis_applied := l_offer_dis_applied(i);
                    v_lines(i).offer_name_snapshot := l_offer_name_snapshot(i);
                    v_lines(i).offer_object_version_number := l_offer_object_version_number(i);
                    v_lines(i).offer_dtl_object_version_number := l_offer_dtl_object_version_number(i);
                    v_client_ids(i) := l_client_id(i);
                end loop;

                if line_count > 0 then
                    bil_invoice_api.expand_bundled_offer_ig_lines(
                        p_visible_lines => v_lines,
                        p_visible_client_ids => v_client_ids,
                        o_full_lines => v_full_lines,
                        o_full_client_ids => v_full_client_ids
                    );
                end if;

                bil_invoice_api.create_full_invoice(
                    p_header => v_header,
                    p_lines => v_full_lines,
                    p_post_payment => 'Y',
                    p_amount_1 => amount_1,
                    p_amount_2 => nvl(amount_2, 0),
                    p_sub_paytype => sub_paytype,
                    p_sub_paytype2 => sub_paytype2,
                    p_allow_partial => 'N',
                    p_allow_overpayment => 'N',
                    p_post_queue => 'Y',
                    p_queue_force => 'N',
                    p_reserv_system_enabled => 'N',
                    p_post_stock => 'Y',
                    p_stock_repost => 'Y',
                    p_build_print_url => 'N',
                    p_send_sms => 'N',
                    o_result => v_result,
                    p_request_id => request_id
                );

                fr_inv_no := v_result.invoice_result.inv_no;
                fr_invdate := v_result.invoice_result.invdate;
                fr_patientno := v_result.invoice_result.patientno;
                fr_curr_code := v_result.invoice_result.curr_code;
                fr_line_count := v_result.invoice_result.line_count;
                fr_total_gross := v_result.invoice_result.total_gross;
                fr_total_discount := v_result.invoice_result.total_discount;
                fr_total_net := v_result.invoice_result.total_net;
                fr_pat_pay := v_result.invoice_result.pat_pay;
                fr_comp_pay := v_result.invoice_result.comp_pay;
                fr_vat_total_pat := v_result.invoice_result.vat_total_pat;
                fr_vat_total_co := v_result.invoice_result.vat_total_co;
                fr_vat_total := v_result.invoice_result.vat_total;
                fr_finaldisc := v_result.invoice_result.finaldisc;
                fr_cash_collected := v_result.invoice_result.cash_collected;
                fr_shift_system_unique := v_result.invoice_result.shift_system_unique;
                fr_payment_posted := v_result.payment_posted;
                fr_queue_posted := v_result.queue_posted;
                fr_stock_posted := v_result.stock_posted;
                fr_print_url_built := v_result.print_url_built;
                fr_sms_sent := v_result.sms_sent;
                fr_message := v_result.message;
                fr_message_send_status := v_result.message_result.send_status;
                fr_message_text := v_result.message_result.message;
            end run;
        begin
            run(
                h_patientno => :h_patientno,
                h_invdate => :h_invdate,
                h_invtypeid => :h_invtypeid,
                h_paytype => :h_paytype,
                h_sub_paytype => :h_sub_paytype,
                h_sub_paytype2 => :h_sub_paytype2,
                h_clinicid => :h_clinicid,
                h_docid => :h_docid,
                h_curr_code => :h_curr_code,
                h_pre_authorization => :h_pre_authorization,
                h_claim_no => :h_claim_no,
                h_claim_flag => :h_claim_flag,
                h_note_no => :h_note_no,
                h_finaldisc_perc => :h_finaldisc_perc,
                h_finaldisc => :h_finaldisc,
                h_amount_1 => :h_amount_1,
                h_amount_2 => :h_amount_2,
                h_add_to_list => :h_add_to_list,
                h_user_no => :h_user_no,
                h_machine_n => :h_machine_n,
                h_info_center_id => :h_info_center_id,
                l_serviceid => :l_serviceid,
                l_qty => :l_qty,
                l_price_override => :l_price_override,
                l_use_price_override => :l_use_price_override,
                l_discount_type => :l_discount_type,
                l_disc => :l_disc,
                l_my_disc => :l_my_disc,
                l_teeth_no => :l_teeth_no,
                l_tooth_surface => :l_tooth_surface,
                l_teeth_no2 => :l_teeth_no2,
                l_pat_serv_req_row_id => :l_pat_serv_req_row_id,
                l_approv_date => :l_approv_date,
                l_approv_validity => :l_approv_validity,
                l_approv_ref_no => :l_approv_ref_no,
                l_claim_no => :l_claim_no,
                l_req_need_a => :l_req_need_a,
                l_req_a_status => :l_req_a_status,
                l_package_service_id => :l_package_service_id,
                l_package_instance_id => :l_package_instance_id,
                l_package_line_role => :l_package_line_role,
                l_package_component_order => :l_package_component_order,
                l_package_parent_line_id => :l_package_parent_line_id,
                l_package_pricing_method => :l_package_pricing_method,
                l_package_definition_token => :l_package_definition_token,
                l_offer_id => :l_offer_id,
                l_offer_dtl_id => :l_offer_dtl_id,
                l_offer_type => :l_offer_type,
                l_offer_instance_id => :l_offer_instance_id,
                l_offer_line_role => :l_offer_line_role,
                l_offer_parent_line_id => :l_offer_parent_line_id,
                l_offer_price_applied => :l_offer_price_applied,
                l_offer_dis_applied => :l_offer_dis_applied,
                l_offer_name_snapshot => :l_offer_name_snapshot,
                l_offer_object_version_number => :l_offer_object_version_number,
                l_offer_dtl_object_version_number => :l_offer_dtl_object_version_number,
                line_count => :line_count,
                l_client_id => :l_client_id,
                amount_1 => :amount_1,
                amount_2 => :amount_2,
                sub_paytype => :sub_paytype,
                sub_paytype2 => :sub_paytype2,
                request_id => :request_id,
                fr_inv_no => :fr_inv_no,
                fr_invdate => :fr_invdate,
                fr_patientno => :fr_patientno,
                fr_curr_code => :fr_curr_code,
                fr_line_count => :fr_line_count,
                fr_total_gross => :fr_total_gross,
                fr_total_discount => :fr_total_discount,
                fr_total_net => :fr_total_net,
                fr_pat_pay => :fr_pat_pay,
                fr_comp_pay => :fr_comp_pay,
                fr_vat_total_pat => :fr_vat_total_pat,
                fr_vat_total_co => :fr_vat_total_co,
                fr_vat_total => :fr_vat_total,
                fr_finaldisc => :fr_finaldisc,
                fr_cash_collected => :fr_cash_collected,
                fr_shift_system_unique => :fr_shift_system_unique,
                fr_payment_posted => :fr_payment_posted,
                fr_queue_posted => :fr_queue_posted,
                fr_stock_posted => :fr_stock_posted,
                fr_print_url_built => :fr_print_url_built,
                fr_sms_sent => :fr_sms_sent,
                fr_message => :fr_message,
                fr_message_send_status => :fr_message_send_status,
                fr_message_text => :fr_message_text
            );
        end;
        """;

    /// <summary>Calls BIL_IMPORT.SET_REQUEST_LINE_SELECTION per row, GET_INVOICE_REQUEST_LINES, CLEAR_REQUEST_INVOICE_SELECTION and TO_ENGINE_LINES; copies no line when the engine lines exceed max_output_lines, while el_count reports the full count.</summary>
    public const string RequestImport = """
        declare
            type t_num is table of number index by pls_integer;
            type t_vc is table of varchar2(4000) index by pls_integer;
            type t_dt is table of date index by pls_integer;
            procedure run(
                patientno in varchar2,
                visit_unique in varchar2,
                paytype in number,
                app_id in number,
                app_session_id in varchar2,
                app_user in varchar2,
                invoice_date in date,
                approval_mode in number,
                req_row_id in t_num,
                req_row_count in number,
                max_output_lines in pls_integer,
                el_serviceid out t_vc,
                el_qty out t_num,
                el_price_override out t_num,
                el_use_price_override out t_vc,
                el_discount_type out t_vc,
                el_disc out t_num,
                el_my_disc out t_num,
                el_teeth_no out t_vc,
                el_tooth_surface out t_vc,
                el_teeth_no2 out t_vc,
                el_pat_serv_req_row_id out t_num,
                el_approv_date out t_dt,
                el_approv_validity out t_num,
                el_approv_ref_no out t_vc,
                el_claim_no out t_vc,
                el_req_need_a out t_num,
                el_req_a_status out t_num,
                el_package_service_id out t_vc,
                el_package_instance_id out t_vc,
                el_package_line_role out t_vc,
                el_package_component_order out t_num,
                el_package_parent_line_id out t_num,
                el_package_pricing_method out t_vc,
                el_package_definition_token out t_vc,
                el_offer_id out t_num,
                el_offer_dtl_id out t_num,
                el_offer_type out t_num,
                el_offer_instance_id out t_vc,
                el_offer_line_role out t_vc,
                el_offer_parent_line_id out t_num,
                el_offer_price_applied out t_num,
                el_offer_dis_applied out t_num,
                el_offer_name_snapshot out t_vc,
                el_offer_object_version_number out t_num,
                el_offer_dtl_object_version_number out t_num,
                el_count out number,
                ir_source_type out varchar2,
                ir_source_count out number,
                ir_imported_count out number,
                ir_skipped_rejected_count out number,
                ir_skipped_need_approval_count out number,
                ir_skipped_invalid_count out number,
                ir_has_price_overrides out varchar2,
                ir_message out varchar2
            ) is
                v_import bil_import.t_import_line_tab;
                v_import_result bil_import.t_import_result;
                v_engine bil_invoice_engine.t_line_input_tab;
                v_idx pls_integer;
                v_out pls_integer := 0;
            begin
                for i in 1 .. req_row_count loop
                    bil_import.set_request_line_selection(
                        p_patientno => patientno,
                        p_visit_unique => visit_unique,
                        p_paytype => paytype,
                        p_app_id => app_id,
                        p_app_session_id => app_session_id,
                        p_app_user => app_user,
                        p_pat_serv_req_row_id => req_row_id(i),
                        p_selected => 1
                    );
                end loop;

                bil_import.get_invoice_request_lines(
                    p_patientno => patientno,
                    p_visit_unique => visit_unique,
                    p_paytype => paytype,
                    p_app_id => app_id,
                    p_app_session_id => app_session_id,
                    p_app_user => app_user,
                    p_invoice_date => invoice_date,
                    p_approval_check_mode => approval_mode,
                    p_raise_on_blocked => 'N',
                    o_lines => v_import,
                    o_result => v_import_result
                );

                bil_import.clear_request_invoice_selection(
                    p_patientno => patientno,
                    p_app_id => app_id,
                    p_app_session_id => app_session_id,
                    p_app_user => app_user
                );

                bil_import.to_engine_lines(
                    p_import_lines => v_import,
                    o_engine_lines => v_engine
                );

                if v_engine.count > max_output_lines then

                    v_idx := null;

                else

                    v_idx := v_engine.first;

                end if;
                while v_idx is not null loop
                    v_out := v_out + 1;
                    el_serviceid(v_out) := v_engine(v_idx).serviceid;
                    el_qty(v_out) := v_engine(v_idx).qty;
                    el_price_override(v_out) := v_engine(v_idx).price_override;
                    el_use_price_override(v_out) := v_engine(v_idx).use_price_override;
                    el_discount_type(v_out) := v_engine(v_idx).discount_type;
                    el_disc(v_out) := v_engine(v_idx).disc;
                    el_my_disc(v_out) := v_engine(v_idx).my_disc;
                    el_teeth_no(v_out) := v_engine(v_idx).teeth_no;
                    el_tooth_surface(v_out) := v_engine(v_idx).tooth_surface;
                    el_teeth_no2(v_out) := v_engine(v_idx).teeth_no2;
                    el_pat_serv_req_row_id(v_out) := v_engine(v_idx).pat_serv_req_row_id;
                    el_approv_date(v_out) := v_engine(v_idx).approv_date;
                    el_approv_validity(v_out) := v_engine(v_idx).approv_validity;
                    el_approv_ref_no(v_out) := v_engine(v_idx).approv_ref_no;
                    el_claim_no(v_out) := v_engine(v_idx).claim_no;
                    el_req_need_a(v_out) := v_engine(v_idx).req_need_a;
                    el_req_a_status(v_out) := v_engine(v_idx).req_a_status;
                    el_package_service_id(v_out) := v_engine(v_idx).package_service_id;
                    el_package_instance_id(v_out) := v_engine(v_idx).package_instance_id;
                    el_package_line_role(v_out) := v_engine(v_idx).package_line_role;
                    el_package_component_order(v_out) := v_engine(v_idx).package_component_order;
                    el_package_parent_line_id(v_out) := v_engine(v_idx).package_parent_line_id;
                    el_package_pricing_method(v_out) := v_engine(v_idx).package_pricing_method;
                    el_package_definition_token(v_out) := v_engine(v_idx).package_definition_token;
                    el_offer_id(v_out) := v_engine(v_idx).offer_id;
                    el_offer_dtl_id(v_out) := v_engine(v_idx).offer_dtl_id;
                    el_offer_type(v_out) := v_engine(v_idx).offer_type;
                    el_offer_instance_id(v_out) := v_engine(v_idx).offer_instance_id;
                    el_offer_line_role(v_out) := v_engine(v_idx).offer_line_role;
                    el_offer_parent_line_id(v_out) := v_engine(v_idx).offer_parent_line_id;
                    el_offer_price_applied(v_out) := v_engine(v_idx).offer_price_applied;
                    el_offer_dis_applied(v_out) := v_engine(v_idx).offer_dis_applied;
                    el_offer_name_snapshot(v_out) := v_engine(v_idx).offer_name_snapshot;
                    el_offer_object_version_number(v_out) := v_engine(v_idx).offer_object_version_number;
                    el_offer_dtl_object_version_number(v_out) := v_engine(v_idx).offer_dtl_object_version_number;
                    v_idx := v_engine.next(v_idx);
                end loop;
                el_count := v_engine.count;
                if v_out = 0 then
                    el_serviceid(1) := null;
                    el_qty(1) := null;
                    el_price_override(1) := null;
                    el_use_price_override(1) := null;
                    el_discount_type(1) := null;
                    el_disc(1) := null;
                    el_my_disc(1) := null;
                    el_teeth_no(1) := null;
                    el_tooth_surface(1) := null;
                    el_teeth_no2(1) := null;
                    el_pat_serv_req_row_id(1) := null;
                    el_approv_date(1) := null;
                    el_approv_validity(1) := null;
                    el_approv_ref_no(1) := null;
                    el_claim_no(1) := null;
                    el_req_need_a(1) := null;
                    el_req_a_status(1) := null;
                    el_package_service_id(1) := null;
                    el_package_instance_id(1) := null;
                    el_package_line_role(1) := null;
                    el_package_component_order(1) := null;
                    el_package_parent_line_id(1) := null;
                    el_package_pricing_method(1) := null;
                    el_package_definition_token(1) := null;
                    el_offer_id(1) := null;
                    el_offer_dtl_id(1) := null;
                    el_offer_type(1) := null;
                    el_offer_instance_id(1) := null;
                    el_offer_line_role(1) := null;
                    el_offer_parent_line_id(1) := null;
                    el_offer_price_applied(1) := null;
                    el_offer_dis_applied(1) := null;
                    el_offer_name_snapshot(1) := null;
                    el_offer_object_version_number(1) := null;
                    el_offer_dtl_object_version_number(1) := null;
                end if;

                ir_source_type := v_import_result.source_type;
                ir_source_count := v_import_result.source_count;
                ir_imported_count := v_import_result.imported_count;
                ir_skipped_rejected_count := v_import_result.skipped_rejected_count;
                ir_skipped_need_approval_count := v_import_result.skipped_need_approval_count;
                ir_skipped_invalid_count := v_import_result.skipped_invalid_count;
                ir_has_price_overrides := v_import_result.has_price_overrides;
                ir_message := v_import_result.message;
            end run;
        begin
            run(
                patientno => :patientno,
                visit_unique => :visit_unique,
                paytype => :paytype,
                app_id => :app_id,
                app_session_id => :app_session_id,
                app_user => :app_user,
                invoice_date => :invoice_date,
                approval_mode => :approval_mode,
                req_row_id => :req_row_id,
                req_row_count => :req_row_count,
                max_output_lines => :max_output_lines,
                el_serviceid => :el_serviceid,
                el_qty => :el_qty,
                el_price_override => :el_price_override,
                el_use_price_override => :el_use_price_override,
                el_discount_type => :el_discount_type,
                el_disc => :el_disc,
                el_my_disc => :el_my_disc,
                el_teeth_no => :el_teeth_no,
                el_tooth_surface => :el_tooth_surface,
                el_teeth_no2 => :el_teeth_no2,
                el_pat_serv_req_row_id => :el_pat_serv_req_row_id,
                el_approv_date => :el_approv_date,
                el_approv_validity => :el_approv_validity,
                el_approv_ref_no => :el_approv_ref_no,
                el_claim_no => :el_claim_no,
                el_req_need_a => :el_req_need_a,
                el_req_a_status => :el_req_a_status,
                el_package_service_id => :el_package_service_id,
                el_package_instance_id => :el_package_instance_id,
                el_package_line_role => :el_package_line_role,
                el_package_component_order => :el_package_component_order,
                el_package_parent_line_id => :el_package_parent_line_id,
                el_package_pricing_method => :el_package_pricing_method,
                el_package_definition_token => :el_package_definition_token,
                el_offer_id => :el_offer_id,
                el_offer_dtl_id => :el_offer_dtl_id,
                el_offer_type => :el_offer_type,
                el_offer_instance_id => :el_offer_instance_id,
                el_offer_line_role => :el_offer_line_role,
                el_offer_parent_line_id => :el_offer_parent_line_id,
                el_offer_price_applied => :el_offer_price_applied,
                el_offer_dis_applied => :el_offer_dis_applied,
                el_offer_name_snapshot => :el_offer_name_snapshot,
                el_offer_object_version_number => :el_offer_object_version_number,
                el_offer_dtl_object_version_number => :el_offer_dtl_object_version_number,
                el_count => :el_count,
                ir_source_type => :ir_source_type,
                ir_source_count => :ir_source_count,
                ir_imported_count => :ir_imported_count,
                ir_skipped_rejected_count => :ir_skipped_rejected_count,
                ir_skipped_need_approval_count => :ir_skipped_need_approval_count,
                ir_skipped_invalid_count => :ir_skipped_invalid_count,
                ir_has_price_overrides => :ir_has_price_overrides,
                ir_message => :ir_message
            );
        end;
        """;

    /// <summary>Calls BIL_IMPORT.GET_VISIT_LINE and TO_ENGINE_LINES; returns the visit line as engine lines; copies no line when they exceed max_output_lines, while el_count reports the full count.</summary>
    public const string VisitLine = """
        declare
            type t_num is table of number index by pls_integer;
            type t_vc is table of varchar2(4000) index by pls_integer;
            type t_dt is table of date index by pls_integer;
            procedure run(
                patientno in varchar2,
                docid in number,
                new_visit_type in varchar2,
                paytype in number,
                clinicid in number,
                info_center_id in varchar2,
                invoice_date in date,
                max_output_lines in pls_integer,
                el_serviceid out t_vc,
                el_qty out t_num,
                el_price_override out t_num,
                el_use_price_override out t_vc,
                el_discount_type out t_vc,
                el_disc out t_num,
                el_my_disc out t_num,
                el_teeth_no out t_vc,
                el_tooth_surface out t_vc,
                el_teeth_no2 out t_vc,
                el_pat_serv_req_row_id out t_num,
                el_approv_date out t_dt,
                el_approv_validity out t_num,
                el_approv_ref_no out t_vc,
                el_claim_no out t_vc,
                el_req_need_a out t_num,
                el_req_a_status out t_num,
                el_package_service_id out t_vc,
                el_package_instance_id out t_vc,
                el_package_line_role out t_vc,
                el_package_component_order out t_num,
                el_package_parent_line_id out t_num,
                el_package_pricing_method out t_vc,
                el_package_definition_token out t_vc,
                el_offer_id out t_num,
                el_offer_dtl_id out t_num,
                el_offer_type out t_num,
                el_offer_instance_id out t_vc,
                el_offer_line_role out t_vc,
                el_offer_parent_line_id out t_num,
                el_offer_price_applied out t_num,
                el_offer_dis_applied out t_num,
                el_offer_name_snapshot out t_vc,
                el_offer_object_version_number out t_num,
                el_offer_dtl_object_version_number out t_num,
                el_count out number,
                ir_source_type out varchar2,
                ir_source_count out number,
                ir_imported_count out number,
                ir_skipped_rejected_count out number,
                ir_skipped_need_approval_count out number,
                ir_skipped_invalid_count out number,
                ir_has_price_overrides out varchar2,
                ir_message out varchar2
            ) is
                v_line bil_import.t_import_line;
                v_import bil_import.t_import_line_tab;
                v_import_result bil_import.t_import_result;
                v_engine bil_invoice_engine.t_line_input_tab;
                v_idx pls_integer;
                v_out pls_integer := 0;
            begin
                bil_import.get_visit_line(
                    p_patientno => patientno,
                    p_docid => docid,
                    p_new_visit_type => new_visit_type,
                    p_paytype => paytype,
                    p_clinicid => clinicid,
                    p_info_center_id => info_center_id,
                    p_invoice_date => invoice_date,
                    o_line => v_line,
                    o_result => v_import_result
                );

                if v_line.serviceid is not null then
                    v_import(1) := v_line;
                end if;

                bil_import.to_engine_lines(
                    p_import_lines => v_import,
                    o_engine_lines => v_engine
                );

                if v_engine.count > max_output_lines then

                    v_idx := null;

                else

                    v_idx := v_engine.first;

                end if;
                while v_idx is not null loop
                    v_out := v_out + 1;
                    el_serviceid(v_out) := v_engine(v_idx).serviceid;
                    el_qty(v_out) := v_engine(v_idx).qty;
                    el_price_override(v_out) := v_engine(v_idx).price_override;
                    el_use_price_override(v_out) := v_engine(v_idx).use_price_override;
                    el_discount_type(v_out) := v_engine(v_idx).discount_type;
                    el_disc(v_out) := v_engine(v_idx).disc;
                    el_my_disc(v_out) := v_engine(v_idx).my_disc;
                    el_teeth_no(v_out) := v_engine(v_idx).teeth_no;
                    el_tooth_surface(v_out) := v_engine(v_idx).tooth_surface;
                    el_teeth_no2(v_out) := v_engine(v_idx).teeth_no2;
                    el_pat_serv_req_row_id(v_out) := v_engine(v_idx).pat_serv_req_row_id;
                    el_approv_date(v_out) := v_engine(v_idx).approv_date;
                    el_approv_validity(v_out) := v_engine(v_idx).approv_validity;
                    el_approv_ref_no(v_out) := v_engine(v_idx).approv_ref_no;
                    el_claim_no(v_out) := v_engine(v_idx).claim_no;
                    el_req_need_a(v_out) := v_engine(v_idx).req_need_a;
                    el_req_a_status(v_out) := v_engine(v_idx).req_a_status;
                    el_package_service_id(v_out) := v_engine(v_idx).package_service_id;
                    el_package_instance_id(v_out) := v_engine(v_idx).package_instance_id;
                    el_package_line_role(v_out) := v_engine(v_idx).package_line_role;
                    el_package_component_order(v_out) := v_engine(v_idx).package_component_order;
                    el_package_parent_line_id(v_out) := v_engine(v_idx).package_parent_line_id;
                    el_package_pricing_method(v_out) := v_engine(v_idx).package_pricing_method;
                    el_package_definition_token(v_out) := v_engine(v_idx).package_definition_token;
                    el_offer_id(v_out) := v_engine(v_idx).offer_id;
                    el_offer_dtl_id(v_out) := v_engine(v_idx).offer_dtl_id;
                    el_offer_type(v_out) := v_engine(v_idx).offer_type;
                    el_offer_instance_id(v_out) := v_engine(v_idx).offer_instance_id;
                    el_offer_line_role(v_out) := v_engine(v_idx).offer_line_role;
                    el_offer_parent_line_id(v_out) := v_engine(v_idx).offer_parent_line_id;
                    el_offer_price_applied(v_out) := v_engine(v_idx).offer_price_applied;
                    el_offer_dis_applied(v_out) := v_engine(v_idx).offer_dis_applied;
                    el_offer_name_snapshot(v_out) := v_engine(v_idx).offer_name_snapshot;
                    el_offer_object_version_number(v_out) := v_engine(v_idx).offer_object_version_number;
                    el_offer_dtl_object_version_number(v_out) := v_engine(v_idx).offer_dtl_object_version_number;
                    v_idx := v_engine.next(v_idx);
                end loop;
                el_count := v_engine.count;
                if v_out = 0 then
                    el_serviceid(1) := null;
                    el_qty(1) := null;
                    el_price_override(1) := null;
                    el_use_price_override(1) := null;
                    el_discount_type(1) := null;
                    el_disc(1) := null;
                    el_my_disc(1) := null;
                    el_teeth_no(1) := null;
                    el_tooth_surface(1) := null;
                    el_teeth_no2(1) := null;
                    el_pat_serv_req_row_id(1) := null;
                    el_approv_date(1) := null;
                    el_approv_validity(1) := null;
                    el_approv_ref_no(1) := null;
                    el_claim_no(1) := null;
                    el_req_need_a(1) := null;
                    el_req_a_status(1) := null;
                    el_package_service_id(1) := null;
                    el_package_instance_id(1) := null;
                    el_package_line_role(1) := null;
                    el_package_component_order(1) := null;
                    el_package_parent_line_id(1) := null;
                    el_package_pricing_method(1) := null;
                    el_package_definition_token(1) := null;
                    el_offer_id(1) := null;
                    el_offer_dtl_id(1) := null;
                    el_offer_type(1) := null;
                    el_offer_instance_id(1) := null;
                    el_offer_line_role(1) := null;
                    el_offer_parent_line_id(1) := null;
                    el_offer_price_applied(1) := null;
                    el_offer_dis_applied(1) := null;
                    el_offer_name_snapshot(1) := null;
                    el_offer_object_version_number(1) := null;
                    el_offer_dtl_object_version_number(1) := null;
                end if;

                ir_source_type := v_import_result.source_type;
                ir_source_count := v_import_result.source_count;
                ir_imported_count := v_import_result.imported_count;
                ir_skipped_rejected_count := v_import_result.skipped_rejected_count;
                ir_skipped_need_approval_count := v_import_result.skipped_need_approval_count;
                ir_skipped_invalid_count := v_import_result.skipped_invalid_count;
                ir_has_price_overrides := v_import_result.has_price_overrides;
                ir_message := v_import_result.message;
            end run;
        begin
            run(
                patientno => :patientno,
                docid => :docid,
                new_visit_type => :new_visit_type,
                paytype => :paytype,
                clinicid => :clinicid,
                info_center_id => :info_center_id,
                invoice_date => :invoice_date,
                max_output_lines => :max_output_lines,
                el_serviceid => :el_serviceid,
                el_qty => :el_qty,
                el_price_override => :el_price_override,
                el_use_price_override => :el_use_price_override,
                el_discount_type => :el_discount_type,
                el_disc => :el_disc,
                el_my_disc => :el_my_disc,
                el_teeth_no => :el_teeth_no,
                el_tooth_surface => :el_tooth_surface,
                el_teeth_no2 => :el_teeth_no2,
                el_pat_serv_req_row_id => :el_pat_serv_req_row_id,
                el_approv_date => :el_approv_date,
                el_approv_validity => :el_approv_validity,
                el_approv_ref_no => :el_approv_ref_no,
                el_claim_no => :el_claim_no,
                el_req_need_a => :el_req_need_a,
                el_req_a_status => :el_req_a_status,
                el_package_service_id => :el_package_service_id,
                el_package_instance_id => :el_package_instance_id,
                el_package_line_role => :el_package_line_role,
                el_package_component_order => :el_package_component_order,
                el_package_parent_line_id => :el_package_parent_line_id,
                el_package_pricing_method => :el_package_pricing_method,
                el_package_definition_token => :el_package_definition_token,
                el_offer_id => :el_offer_id,
                el_offer_dtl_id => :el_offer_dtl_id,
                el_offer_type => :el_offer_type,
                el_offer_instance_id => :el_offer_instance_id,
                el_offer_line_role => :el_offer_line_role,
                el_offer_parent_line_id => :el_offer_parent_line_id,
                el_offer_price_applied => :el_offer_price_applied,
                el_offer_dis_applied => :el_offer_dis_applied,
                el_offer_name_snapshot => :el_offer_name_snapshot,
                el_offer_object_version_number => :el_offer_object_version_number,
                el_offer_dtl_object_version_number => :el_offer_dtl_object_version_number,
                el_count => :el_count,
                ir_source_type => :ir_source_type,
                ir_source_count => :ir_source_count,
                ir_imported_count => :ir_imported_count,
                ir_skipped_rejected_count => :ir_skipped_rejected_count,
                ir_skipped_need_approval_count => :ir_skipped_need_approval_count,
                ir_skipped_invalid_count => :ir_skipped_invalid_count,
                ir_has_price_overrides => :ir_has_price_overrides,
                ir_message => :ir_message
            );
        end;
        """;

    /// <summary>Calls BIL_INVOICE_API.GET_PACKAGE_LINES and BIL_IMPORT.TO_ENGINE_LINES; returns the package components as engine lines; copies no line when they exceed max_output_lines, while el_count reports the full count.</summary>
    public const string PackageLines = """
        declare
            type t_num is table of number index by pls_integer;
            type t_vc is table of varchar2(4000) index by pls_integer;
            type t_dt is table of date index by pls_integer;
            procedure run(
                package_serviceid in varchar2,
                list_id in number,
                parent_source_id in varchar2,
                max_output_lines in pls_integer,
                el_serviceid out t_vc,
                el_qty out t_num,
                el_price_override out t_num,
                el_use_price_override out t_vc,
                el_discount_type out t_vc,
                el_disc out t_num,
                el_my_disc out t_num,
                el_teeth_no out t_vc,
                el_tooth_surface out t_vc,
                el_teeth_no2 out t_vc,
                el_pat_serv_req_row_id out t_num,
                el_approv_date out t_dt,
                el_approv_validity out t_num,
                el_approv_ref_no out t_vc,
                el_claim_no out t_vc,
                el_req_need_a out t_num,
                el_req_a_status out t_num,
                el_package_service_id out t_vc,
                el_package_instance_id out t_vc,
                el_package_line_role out t_vc,
                el_package_component_order out t_num,
                el_package_parent_line_id out t_num,
                el_package_pricing_method out t_vc,
                el_package_definition_token out t_vc,
                el_offer_id out t_num,
                el_offer_dtl_id out t_num,
                el_offer_type out t_num,
                el_offer_instance_id out t_vc,
                el_offer_line_role out t_vc,
                el_offer_parent_line_id out t_num,
                el_offer_price_applied out t_num,
                el_offer_dis_applied out t_num,
                el_offer_name_snapshot out t_vc,
                el_offer_object_version_number out t_num,
                el_offer_dtl_object_version_number out t_num,
                el_count out number,
                ir_source_type out varchar2,
                ir_source_count out number,
                ir_imported_count out number,
                ir_skipped_rejected_count out number,
                ir_skipped_need_approval_count out number,
                ir_skipped_invalid_count out number,
                ir_has_price_overrides out varchar2,
                ir_message out varchar2
            ) is
                v_import bil_import.t_import_line_tab;
                v_import_result bil_import.t_import_result;
                v_engine bil_invoice_engine.t_line_input_tab;
                v_idx pls_integer;
                v_out pls_integer := 0;
            begin
                bil_invoice_api.get_package_lines(
                    p_package_serviceid => package_serviceid,
                    p_list_id => list_id,
                    p_parent_source_id => parent_source_id,
                    o_lines => v_import,
                    o_result => v_import_result
                );

                bil_import.to_engine_lines(
                    p_import_lines => v_import,
                    o_engine_lines => v_engine
                );

                if v_engine.count > max_output_lines then

                    v_idx := null;

                else

                    v_idx := v_engine.first;

                end if;
                while v_idx is not null loop
                    v_out := v_out + 1;
                    el_serviceid(v_out) := v_engine(v_idx).serviceid;
                    el_qty(v_out) := v_engine(v_idx).qty;
                    el_price_override(v_out) := v_engine(v_idx).price_override;
                    el_use_price_override(v_out) := v_engine(v_idx).use_price_override;
                    el_discount_type(v_out) := v_engine(v_idx).discount_type;
                    el_disc(v_out) := v_engine(v_idx).disc;
                    el_my_disc(v_out) := v_engine(v_idx).my_disc;
                    el_teeth_no(v_out) := v_engine(v_idx).teeth_no;
                    el_tooth_surface(v_out) := v_engine(v_idx).tooth_surface;
                    el_teeth_no2(v_out) := v_engine(v_idx).teeth_no2;
                    el_pat_serv_req_row_id(v_out) := v_engine(v_idx).pat_serv_req_row_id;
                    el_approv_date(v_out) := v_engine(v_idx).approv_date;
                    el_approv_validity(v_out) := v_engine(v_idx).approv_validity;
                    el_approv_ref_no(v_out) := v_engine(v_idx).approv_ref_no;
                    el_claim_no(v_out) := v_engine(v_idx).claim_no;
                    el_req_need_a(v_out) := v_engine(v_idx).req_need_a;
                    el_req_a_status(v_out) := v_engine(v_idx).req_a_status;
                    el_package_service_id(v_out) := v_engine(v_idx).package_service_id;
                    el_package_instance_id(v_out) := v_engine(v_idx).package_instance_id;
                    el_package_line_role(v_out) := v_engine(v_idx).package_line_role;
                    el_package_component_order(v_out) := v_engine(v_idx).package_component_order;
                    el_package_parent_line_id(v_out) := v_engine(v_idx).package_parent_line_id;
                    el_package_pricing_method(v_out) := v_engine(v_idx).package_pricing_method;
                    el_package_definition_token(v_out) := v_engine(v_idx).package_definition_token;
                    el_offer_id(v_out) := v_engine(v_idx).offer_id;
                    el_offer_dtl_id(v_out) := v_engine(v_idx).offer_dtl_id;
                    el_offer_type(v_out) := v_engine(v_idx).offer_type;
                    el_offer_instance_id(v_out) := v_engine(v_idx).offer_instance_id;
                    el_offer_line_role(v_out) := v_engine(v_idx).offer_line_role;
                    el_offer_parent_line_id(v_out) := v_engine(v_idx).offer_parent_line_id;
                    el_offer_price_applied(v_out) := v_engine(v_idx).offer_price_applied;
                    el_offer_dis_applied(v_out) := v_engine(v_idx).offer_dis_applied;
                    el_offer_name_snapshot(v_out) := v_engine(v_idx).offer_name_snapshot;
                    el_offer_object_version_number(v_out) := v_engine(v_idx).offer_object_version_number;
                    el_offer_dtl_object_version_number(v_out) := v_engine(v_idx).offer_dtl_object_version_number;
                    v_idx := v_engine.next(v_idx);
                end loop;
                el_count := v_engine.count;
                if v_out = 0 then
                    el_serviceid(1) := null;
                    el_qty(1) := null;
                    el_price_override(1) := null;
                    el_use_price_override(1) := null;
                    el_discount_type(1) := null;
                    el_disc(1) := null;
                    el_my_disc(1) := null;
                    el_teeth_no(1) := null;
                    el_tooth_surface(1) := null;
                    el_teeth_no2(1) := null;
                    el_pat_serv_req_row_id(1) := null;
                    el_approv_date(1) := null;
                    el_approv_validity(1) := null;
                    el_approv_ref_no(1) := null;
                    el_claim_no(1) := null;
                    el_req_need_a(1) := null;
                    el_req_a_status(1) := null;
                    el_package_service_id(1) := null;
                    el_package_instance_id(1) := null;
                    el_package_line_role(1) := null;
                    el_package_component_order(1) := null;
                    el_package_parent_line_id(1) := null;
                    el_package_pricing_method(1) := null;
                    el_package_definition_token(1) := null;
                    el_offer_id(1) := null;
                    el_offer_dtl_id(1) := null;
                    el_offer_type(1) := null;
                    el_offer_instance_id(1) := null;
                    el_offer_line_role(1) := null;
                    el_offer_parent_line_id(1) := null;
                    el_offer_price_applied(1) := null;
                    el_offer_dis_applied(1) := null;
                    el_offer_name_snapshot(1) := null;
                    el_offer_object_version_number(1) := null;
                    el_offer_dtl_object_version_number(1) := null;
                end if;

                ir_source_type := v_import_result.source_type;
                ir_source_count := v_import_result.source_count;
                ir_imported_count := v_import_result.imported_count;
                ir_skipped_rejected_count := v_import_result.skipped_rejected_count;
                ir_skipped_need_approval_count := v_import_result.skipped_need_approval_count;
                ir_skipped_invalid_count := v_import_result.skipped_invalid_count;
                ir_has_price_overrides := v_import_result.has_price_overrides;
                ir_message := v_import_result.message;
            end run;
        begin
            run(
                package_serviceid => :package_serviceid,
                list_id => :list_id,
                parent_source_id => :parent_source_id,
                max_output_lines => :max_output_lines,
                el_serviceid => :el_serviceid,
                el_qty => :el_qty,
                el_price_override => :el_price_override,
                el_use_price_override => :el_use_price_override,
                el_discount_type => :el_discount_type,
                el_disc => :el_disc,
                el_my_disc => :el_my_disc,
                el_teeth_no => :el_teeth_no,
                el_tooth_surface => :el_tooth_surface,
                el_teeth_no2 => :el_teeth_no2,
                el_pat_serv_req_row_id => :el_pat_serv_req_row_id,
                el_approv_date => :el_approv_date,
                el_approv_validity => :el_approv_validity,
                el_approv_ref_no => :el_approv_ref_no,
                el_claim_no => :el_claim_no,
                el_req_need_a => :el_req_need_a,
                el_req_a_status => :el_req_a_status,
                el_package_service_id => :el_package_service_id,
                el_package_instance_id => :el_package_instance_id,
                el_package_line_role => :el_package_line_role,
                el_package_component_order => :el_package_component_order,
                el_package_parent_line_id => :el_package_parent_line_id,
                el_package_pricing_method => :el_package_pricing_method,
                el_package_definition_token => :el_package_definition_token,
                el_offer_id => :el_offer_id,
                el_offer_dtl_id => :el_offer_dtl_id,
                el_offer_type => :el_offer_type,
                el_offer_instance_id => :el_offer_instance_id,
                el_offer_line_role => :el_offer_line_role,
                el_offer_parent_line_id => :el_offer_parent_line_id,
                el_offer_price_applied => :el_offer_price_applied,
                el_offer_dis_applied => :el_offer_dis_applied,
                el_offer_name_snapshot => :el_offer_name_snapshot,
                el_offer_object_version_number => :el_offer_object_version_number,
                el_offer_dtl_object_version_number => :el_offer_dtl_object_version_number,
                el_count => :el_count,
                ir_source_type => :ir_source_type,
                ir_source_count => :ir_source_count,
                ir_imported_count => :ir_imported_count,
                ir_skipped_rejected_count => :ir_skipped_rejected_count,
                ir_skipped_need_approval_count => :ir_skipped_need_approval_count,
                ir_skipped_invalid_count => :ir_skipped_invalid_count,
                ir_has_price_overrides => :ir_has_price_overrides,
                ir_message => :ir_message
            );
        end;
        """;

    /// <summary>Calls BIL_INVOICE_API.GET_BUNDLED_OFFER_IG_LINES; returns the bundled offer as preview lines; copies no line when they exceed max_output_lines, while pl_count reports the full count.</summary>
    public const string BundledOffer = """
        declare
            type t_num is table of number index by pls_integer;
            type t_vc is table of varchar2(4000) index by pls_integer;
            type t_dt is table of date index by pls_integer;
            procedure run(
                patientno in varchar2,
                paytype in number,
                invoice_date in date,
                info_center_id in varchar2,
                offer_id in number,
                bundle_qty in number,
                max_output_lines in pls_integer,
                pl_client_id out t_vc,
                pl_line_no out t_num,
                pl_serviceid out t_vc,
                pl_servicedesc out t_vc,
                pl_catid out t_num,
                pl_list_id out t_num,
                pl_curr_code out t_vc,
                pl_qty out t_num,
                pl_price out t_num,
                pl_plan_discount_pct out t_num,
                pl_plan_discount_amount out t_num,
                pl_manual_discount_type out t_vc,
                pl_manual_discount_pct out t_num,
                pl_manual_discount_amount out t_num,
                pl_discount_source out t_vc,
                pl_disc out t_num,
                pl_my_disc out t_num,
                pl_my_price out t_num,
                pl_my_net out t_num,
                pl_the_pay out t_num,
                pl_the_comp out t_num,
                pl_vat_rate out t_num,
                pl_vat_val_pat out t_num,
                pl_vat_val_co out t_num,
                pl_vat_val_pat_ex out t_num,
                pl_req_need_a out t_num,
                pl_req_a_status out t_num,
                pl_allow_manual_discount out t_vc,
                pl_allow_price_override out t_vc,
                pl_package_service_id out t_vc,
                pl_package_instance_id out t_vc,
                pl_package_line_role out t_vc,
                pl_package_component_order out t_num,
                pl_package_parent_line_id out t_num,
                pl_package_pricing_method out t_vc,
                pl_package_definition_token out t_vc,
                pl_offer_id out t_num,
                pl_offer_dtl_id out t_num,
                pl_offer_type out t_num,
                pl_offer_instance_id out t_vc,
                pl_offer_line_role out t_vc,
                pl_offer_parent_line_id out t_num,
                pl_offer_price_applied out t_num,
                pl_offer_dis_applied out t_num,
                pl_offer_name_snapshot out t_vc,
                pl_offer_object_version_number out t_num,
                pl_offer_dtl_object_version_number out t_num,
                pl_count out number
            ) is
                v_preview bil_invoice_api.t_editable_preview_line_tab;
                v_idx pls_integer;
                v_out pls_integer := 0;
            begin
                bil_invoice_api.get_bundled_offer_ig_lines(
                    p_patientno => patientno,
                    p_paytype => paytype,
                    p_invoice_date => invoice_date,
                    p_info_center_id => info_center_id,
                    p_offer_id => offer_id,
                    p_bundle_qty => bundle_qty,
                    o_lines => v_preview
                );

                if v_preview.count > max_output_lines then

                    v_idx := null;

                else

                    v_idx := v_preview.first;

                end if;
                while v_idx is not null loop
                    v_out := v_out + 1;
                    pl_client_id(v_out) := v_preview(v_idx).client_id;
                    pl_line_no(v_out) := v_preview(v_idx).line_no;
                    pl_serviceid(v_out) := v_preview(v_idx).serviceid;
                    pl_servicedesc(v_out) := v_preview(v_idx).servicedesc;
                    pl_catid(v_out) := v_preview(v_idx).catid;
                    pl_list_id(v_out) := v_preview(v_idx).list_id;
                    pl_curr_code(v_out) := v_preview(v_idx).curr_code;
                    pl_qty(v_out) := v_preview(v_idx).qty;
                    pl_price(v_out) := v_preview(v_idx).price;
                    pl_plan_discount_pct(v_out) := v_preview(v_idx).plan_discount_pct;
                    pl_plan_discount_amount(v_out) := v_preview(v_idx).plan_discount_amount;
                    pl_manual_discount_type(v_out) := v_preview(v_idx).manual_discount_type;
                    pl_manual_discount_pct(v_out) := v_preview(v_idx).manual_discount_pct;
                    pl_manual_discount_amount(v_out) := v_preview(v_idx).manual_discount_amount;
                    pl_discount_source(v_out) := v_preview(v_idx).discount_source;
                    pl_disc(v_out) := v_preview(v_idx).disc;
                    pl_my_disc(v_out) := v_preview(v_idx).my_disc;
                    pl_my_price(v_out) := v_preview(v_idx).my_price;
                    pl_my_net(v_out) := v_preview(v_idx).my_net;
                    pl_the_pay(v_out) := v_preview(v_idx).the_pay;
                    pl_the_comp(v_out) := v_preview(v_idx).the_comp;
                    pl_vat_rate(v_out) := v_preview(v_idx).vat_rate;
                    pl_vat_val_pat(v_out) := v_preview(v_idx).vat_val_pat;
                    pl_vat_val_co(v_out) := v_preview(v_idx).vat_val_co;
                    pl_vat_val_pat_ex(v_out) := v_preview(v_idx).vat_val_pat_ex;
                    pl_req_need_a(v_out) := v_preview(v_idx).req_need_a;
                    pl_req_a_status(v_out) := v_preview(v_idx).req_a_status;
                    pl_allow_manual_discount(v_out) := v_preview(v_idx).allow_manual_discount;
                    pl_allow_price_override(v_out) := v_preview(v_idx).allow_price_override;
                    pl_package_service_id(v_out) := v_preview(v_idx).package_service_id;
                    pl_package_instance_id(v_out) := v_preview(v_idx).package_instance_id;
                    pl_package_line_role(v_out) := v_preview(v_idx).package_line_role;
                    pl_package_component_order(v_out) := v_preview(v_idx).package_component_order;
                    pl_package_parent_line_id(v_out) := v_preview(v_idx).package_parent_line_id;
                    pl_package_pricing_method(v_out) := v_preview(v_idx).package_pricing_method;
                    pl_package_definition_token(v_out) := v_preview(v_idx).package_definition_token;
                    pl_offer_id(v_out) := v_preview(v_idx).offer_id;
                    pl_offer_dtl_id(v_out) := v_preview(v_idx).offer_dtl_id;
                    pl_offer_type(v_out) := v_preview(v_idx).offer_type;
                    pl_offer_instance_id(v_out) := v_preview(v_idx).offer_instance_id;
                    pl_offer_line_role(v_out) := v_preview(v_idx).offer_line_role;
                    pl_offer_parent_line_id(v_out) := v_preview(v_idx).offer_parent_line_id;
                    pl_offer_price_applied(v_out) := v_preview(v_idx).offer_price_applied;
                    pl_offer_dis_applied(v_out) := v_preview(v_idx).offer_dis_applied;
                    pl_offer_name_snapshot(v_out) := v_preview(v_idx).offer_name_snapshot;
                    pl_offer_object_version_number(v_out) := v_preview(v_idx).offer_object_version_number;
                    pl_offer_dtl_object_version_number(v_out) := v_preview(v_idx).offer_dtl_object_version_number;
                    v_idx := v_preview.next(v_idx);
                end loop;
                pl_count := v_preview.count;
                if v_out = 0 then
                    pl_client_id(1) := null;
                    pl_line_no(1) := null;
                    pl_serviceid(1) := null;
                    pl_servicedesc(1) := null;
                    pl_catid(1) := null;
                    pl_list_id(1) := null;
                    pl_curr_code(1) := null;
                    pl_qty(1) := null;
                    pl_price(1) := null;
                    pl_plan_discount_pct(1) := null;
                    pl_plan_discount_amount(1) := null;
                    pl_manual_discount_type(1) := null;
                    pl_manual_discount_pct(1) := null;
                    pl_manual_discount_amount(1) := null;
                    pl_discount_source(1) := null;
                    pl_disc(1) := null;
                    pl_my_disc(1) := null;
                    pl_my_price(1) := null;
                    pl_my_net(1) := null;
                    pl_the_pay(1) := null;
                    pl_the_comp(1) := null;
                    pl_vat_rate(1) := null;
                    pl_vat_val_pat(1) := null;
                    pl_vat_val_co(1) := null;
                    pl_vat_val_pat_ex(1) := null;
                    pl_req_need_a(1) := null;
                    pl_req_a_status(1) := null;
                    pl_allow_manual_discount(1) := null;
                    pl_allow_price_override(1) := null;
                    pl_package_service_id(1) := null;
                    pl_package_instance_id(1) := null;
                    pl_package_line_role(1) := null;
                    pl_package_component_order(1) := null;
                    pl_package_parent_line_id(1) := null;
                    pl_package_pricing_method(1) := null;
                    pl_package_definition_token(1) := null;
                    pl_offer_id(1) := null;
                    pl_offer_dtl_id(1) := null;
                    pl_offer_type(1) := null;
                    pl_offer_instance_id(1) := null;
                    pl_offer_line_role(1) := null;
                    pl_offer_parent_line_id(1) := null;
                    pl_offer_price_applied(1) := null;
                    pl_offer_dis_applied(1) := null;
                    pl_offer_name_snapshot(1) := null;
                    pl_offer_object_version_number(1) := null;
                    pl_offer_dtl_object_version_number(1) := null;
                end if;
            end run;
        begin
            run(
                patientno => :patientno,
                paytype => :paytype,
                invoice_date => :invoice_date,
                info_center_id => :info_center_id,
                offer_id => :offer_id,
                bundle_qty => :bundle_qty,
                max_output_lines => :max_output_lines,
                pl_client_id => :pl_client_id,
                pl_line_no => :pl_line_no,
                pl_serviceid => :pl_serviceid,
                pl_servicedesc => :pl_servicedesc,
                pl_catid => :pl_catid,
                pl_list_id => :pl_list_id,
                pl_curr_code => :pl_curr_code,
                pl_qty => :pl_qty,
                pl_price => :pl_price,
                pl_plan_discount_pct => :pl_plan_discount_pct,
                pl_plan_discount_amount => :pl_plan_discount_amount,
                pl_manual_discount_type => :pl_manual_discount_type,
                pl_manual_discount_pct => :pl_manual_discount_pct,
                pl_manual_discount_amount => :pl_manual_discount_amount,
                pl_discount_source => :pl_discount_source,
                pl_disc => :pl_disc,
                pl_my_disc => :pl_my_disc,
                pl_my_price => :pl_my_price,
                pl_my_net => :pl_my_net,
                pl_the_pay => :pl_the_pay,
                pl_the_comp => :pl_the_comp,
                pl_vat_rate => :pl_vat_rate,
                pl_vat_val_pat => :pl_vat_val_pat,
                pl_vat_val_co => :pl_vat_val_co,
                pl_vat_val_pat_ex => :pl_vat_val_pat_ex,
                pl_req_need_a => :pl_req_need_a,
                pl_req_a_status => :pl_req_a_status,
                pl_allow_manual_discount => :pl_allow_manual_discount,
                pl_allow_price_override => :pl_allow_price_override,
                pl_package_service_id => :pl_package_service_id,
                pl_package_instance_id => :pl_package_instance_id,
                pl_package_line_role => :pl_package_line_role,
                pl_package_component_order => :pl_package_component_order,
                pl_package_parent_line_id => :pl_package_parent_line_id,
                pl_package_pricing_method => :pl_package_pricing_method,
                pl_package_definition_token => :pl_package_definition_token,
                pl_offer_id => :pl_offer_id,
                pl_offer_dtl_id => :pl_offer_dtl_id,
                pl_offer_type => :pl_offer_type,
                pl_offer_instance_id => :pl_offer_instance_id,
                pl_offer_line_role => :pl_offer_line_role,
                pl_offer_parent_line_id => :pl_offer_parent_line_id,
                pl_offer_price_applied => :pl_offer_price_applied,
                pl_offer_dis_applied => :pl_offer_dis_applied,
                pl_offer_name_snapshot => :pl_offer_name_snapshot,
                pl_offer_object_version_number => :pl_offer_object_version_number,
                pl_offer_dtl_object_version_number => :pl_offer_dtl_object_version_number,
                pl_count => :pl_count
            );
        end;
        """;
}
