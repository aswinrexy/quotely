"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Field, Input, Textarea } from "@/components/ui/field";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import {
  TRADE_DELIVERY_TERMS,
  TRADE_PAYMENT_TERMS,
  type TradeProfile,
} from "@/types";

/**
 * Import/export settings: the identifiers and defaults a business would otherwise retype on every
 * document. Filled onto a new document when it is created, and overridable there.
 */
export default function TradeSettingsPage() {
  const toast = useToast();
  const [profile, setProfile] = useState<TradeProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [form, setForm] = useState({
    iecNumber: "",
    gstNumber: "",
    panNumber: "",
    apedaRegistrationNumber: "",
    apedaValidUntil: "",
    partyNameOverride: "",
    partyAddressOverride: "",
    defaultCountryOfOrigin: "",
    defaultTermsOfDelivery: "",
    defaultTermsOfPayment: "",
    defaultPricingTerm: "",
    defaultPortOfLoading: "",
    defaultPreCarriageBy: "",
    defaultHeaderDeclarations: "",
    defaultFooterDeclaration: "",
    defaultAuthorisedSignatory: "",
    defaultCurrency: "",
  });

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const loaded = await api.get<TradeProfile>("/api/import-export/profile");
      setProfile(loaded);
      setForm({
        iecNumber: loaded.iecNumber ?? "",
        gstNumber: loaded.gstNumber ?? "",
        panNumber: loaded.panNumber ?? "",
        apedaRegistrationNumber: loaded.apedaRegistrationNumber ?? "",
        apedaValidUntil: loaded.apedaValidUntil ?? "",
        partyNameOverride: loaded.partyNameOverride ?? "",
        partyAddressOverride: loaded.partyAddressOverride ?? "",
        defaultCountryOfOrigin: loaded.defaultCountryOfOrigin ?? "",
        defaultTermsOfDelivery: loaded.defaultTermsOfDelivery ?? "",
        defaultTermsOfPayment: loaded.defaultTermsOfPayment ?? "",
        defaultPricingTerm: loaded.defaultPricingTerm ?? "",
        defaultPortOfLoading: loaded.defaultPortOfLoading ?? "",
        defaultPreCarriageBy: loaded.defaultPreCarriageBy ?? "",
        defaultHeaderDeclarations: loaded.defaultHeaderDeclarations ?? "",
        defaultFooterDeclaration: loaded.defaultFooterDeclaration ?? "",
        defaultAuthorisedSignatory: loaded.defaultAuthorisedSignatory ?? "",
        defaultCurrency: loaded.defaultCurrency ?? "",
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "We couldn't load your settings.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  function set(key: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function save() {
    setSaving(true);
    try {
      const payload = Object.fromEntries(
        Object.entries(form).map(([key, value]) => [key, value.trim() === "" ? null : value.trim()]),
      );
      setProfile(await api.put<TradeProfile>("/api/import-export/profile", payload));
      toast("Import / export settings saved.", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save your settings.", "error");
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <DetailSkeleton />;
  if (error) return <ErrorState message={error} onRetry={load} />;

  return (
    <>
      <PageHeader
        title="Import / Export settings"
        description="Entered once, filled onto every trade document. You can change any of it per document."
      />

      <div className="space-y-6">
        <Card>
          <CardHeader
            title="Registrations"
            description="Stored exactly as you type them. Quotely does not verify these against any register."
          />
          <CardBody className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="IEC number" htmlFor="iecNumber">
              <Input
                id="iecNumber"
                value={form.iecNumber}
                onChange={(e) => set("iecNumber", e.target.value)}
              />
            </Field>
            <Field label="GST number" htmlFor="gstNumber">
              <Input
                id="gstNumber"
                value={form.gstNumber}
                onChange={(e) => set("gstNumber", e.target.value)}
              />
            </Field>
            <Field label="PAN number" htmlFor="panNumber">
              <Input
                id="panNumber"
                value={form.panNumber}
                onChange={(e) => set("panNumber", e.target.value)}
              />
            </Field>
            <Field label="APEDA registration" htmlFor="apedaRegistrationNumber">
              <Input
                id="apedaRegistrationNumber"
                value={form.apedaRegistrationNumber}
                onChange={(e) => set("apedaRegistrationNumber", e.target.value)}
              />
            </Field>
            <Field label="APEDA valid until" htmlFor="apedaValidUntil">
              <Input
                id="apedaValidUntil"
                type="date"
                value={form.apedaValidUntil}
                onChange={(e) => set("apedaValidUntil", e.target.value)}
              />
            </Field>
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Document defaults"
            description="Leave a field blank to use your business profile instead."
          />
          <CardBody className="space-y-5">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field
                label="Exporter / importer name"
                htmlFor="partyNameOverride"
                hint="Only if it differs from your business profile."
              >
                <Input
                  id="partyNameOverride"
                  value={form.partyNameOverride}
                  onChange={(e) => set("partyNameOverride", e.target.value)}
                />
              </Field>
              <Field label="Exporter / importer address" htmlFor="partyAddressOverride">
                <Textarea
                  id="partyAddressOverride"
                  rows={2}
                  value={form.partyAddressOverride}
                  onChange={(e) => set("partyAddressOverride", e.target.value)}
                />
              </Field>
            </div>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <Field label="Country of origin" htmlFor="defaultCountryOfOrigin">
                <Input
                  id="defaultCountryOfOrigin"
                  value={form.defaultCountryOfOrigin}
                  onChange={(e) => set("defaultCountryOfOrigin", e.target.value)}
                />
              </Field>
              <Field label="Port of loading" htmlFor="defaultPortOfLoading">
                <Input
                  id="defaultPortOfLoading"
                  value={form.defaultPortOfLoading}
                  onChange={(e) => set("defaultPortOfLoading", e.target.value)}
                />
              </Field>
              <Field label="Pre-carriage by" htmlFor="defaultPreCarriageBy">
                <Input
                  id="defaultPreCarriageBy"
                  value={form.defaultPreCarriageBy}
                  onChange={(e) => set("defaultPreCarriageBy", e.target.value)}
                />
              </Field>
              <Field label="Terms of delivery" htmlFor="defaultTermsOfDelivery">
                <Input
                  id="defaultTermsOfDelivery"
                  list="settings-delivery-terms"
                  value={form.defaultTermsOfDelivery}
                  onChange={(e) => set("defaultTermsOfDelivery", e.target.value)}
                />
                <datalist id="settings-delivery-terms">
                  {TRADE_DELIVERY_TERMS.map((term) => (
                    <option key={term} value={term} />
                  ))}
                </datalist>
              </Field>
              <Field label="Terms of payment" htmlFor="defaultTermsOfPayment">
                <Input
                  id="defaultTermsOfPayment"
                  list="settings-payment-terms"
                  value={form.defaultTermsOfPayment}
                  onChange={(e) => set("defaultTermsOfPayment", e.target.value)}
                />
                <datalist id="settings-payment-terms">
                  {TRADE_PAYMENT_TERMS.map((term) => (
                    <option key={term} value={term} />
                  ))}
                </datalist>
              </Field>
              <Field label="Pricing term" htmlFor="defaultPricingTerm">
                <Input
                  id="defaultPricingTerm"
                  list="settings-delivery-terms"
                  value={form.defaultPricingTerm}
                  onChange={(e) => set("defaultPricingTerm", e.target.value)}
                />
              </Field>
              <Field
                label="Currency"
                htmlFor="defaultCurrency"
                hint="Trade is often invoiced in a different currency from domestic work."
              >
                <Input
                  id="defaultCurrency"
                  maxLength={3}
                  value={form.defaultCurrency}
                  onChange={(e) => set("defaultCurrency", e.target.value.toUpperCase())}
                />
              </Field>
              <Field label="Authorised signatory" htmlFor="defaultAuthorisedSignatory">
                <Input
                  id="defaultAuthorisedSignatory"
                  value={form.defaultAuthorisedSignatory}
                  onChange={(e) => set("defaultAuthorisedSignatory", e.target.value)}
                />
              </Field>
            </div>
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Declarations"
            description="Printed on your documents, in your words."
          />
          <CardBody className="space-y-5">
            {/*
              Said plainly, because it matters. The suggested wording below is text some exporters
              use; whether any of it applies to a given business, shipment or notification is not
              something Quotely can know. Nothing is applied unless it is chosen.
            */}
            <p className="rounded-card border border-ash bg-paper px-4 py-3 text-body text-steel">
              These are your declarations, printed as you write them. Quotely does not provide tax
              or legal advice — the suggestions below are a starting point, not a recommendation.
            </p>

            <Field
              label="Header declarations"
              htmlFor="defaultHeaderDeclarations"
              hint="One per line. Printed under the document title."
            >
              <Textarea
                id="defaultHeaderDeclarations"
                rows={3}
                value={form.defaultHeaderDeclarations}
                onChange={(e) => set("defaultHeaderDeclarations", e.target.value)}
              />
            </Field>

            {profile && profile.suggestedHeaderDeclarations.length > 0 && (
              <div className="flex flex-wrap gap-2">
                {profile.suggestedHeaderDeclarations.map((suggestion) => (
                  <button
                    key={suggestion}
                    type="button"
                    onClick={() =>
                      set(
                        "defaultHeaderDeclarations",
                        form.defaultHeaderDeclarations
                          ? `${form.defaultHeaderDeclarations}\n${suggestion}`
                          : suggestion,
                      )
                    }
                    className="rounded-btn border border-ash px-2.5 py-1.5 text-left text-caption text-steel hover:bg-paper hover:text-charcoal"
                  >
                    + {suggestion}
                  </button>
                ))}
              </div>
            )}

            <Field
              label="Declaration"
              htmlFor="defaultFooterDeclaration"
              hint="Printed at the foot of the document."
            >
              <Textarea
                id="defaultFooterDeclaration"
                rows={2}
                value={form.defaultFooterDeclaration}
                onChange={(e) => set("defaultFooterDeclaration", e.target.value)}
              />
            </Field>

            {profile?.suggestedFooterDeclaration && (
              <button
                type="button"
                onClick={() => set("defaultFooterDeclaration", profile.suggestedFooterDeclaration)}
                className="rounded-btn border border-ash px-2.5 py-1.5 text-left text-caption text-steel hover:bg-paper hover:text-charcoal"
              >
                + {profile.suggestedFooterDeclaration}
              </button>
            )}
          </CardBody>
        </Card>

        <div className="flex justify-end">
          <Button onClick={save} loading={saving}>
            Save settings
          </Button>
        </div>
      </div>
    </>
  );
}
