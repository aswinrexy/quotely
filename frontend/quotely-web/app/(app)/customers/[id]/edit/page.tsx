"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { CustomerForm } from "@/components/app/customer-form";
import { PageHeader } from "@/components/app/page-header";
import { Card } from "@/components/ui/card";
import { ErrorState, LoadingState } from "@/components/ui/states";
import type { Customer } from "@/types";

export default function EditCustomerPage() {
  const { id } = useParams<{ id: string }>();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<Customer>(`/api/customers/${id}`)
      .then(setCustomer)
      .catch((err) => setError(err instanceof Error ? err.message : "Could not load the customer."));
  }, [id]);

  return (
    <>
      <PageHeader title="Edit customer" />
      {error ? (
        <Card>
          <ErrorState message={error} />
        </Card>
      ) : !customer ? (
        <Card>
          <LoadingState />
        </Card>
      ) : (
        <CustomerForm customer={customer} />
      )}
    </>
  );
}
