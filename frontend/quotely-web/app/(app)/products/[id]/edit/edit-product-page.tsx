"use client";

import { useEffect, useState } from "react";
import { useRouteParam } from "@/lib/route-param";
import { api } from "@/lib/api";
import { ProductForm } from "@/components/app/product-form";
import { PageHeader } from "@/components/app/page-header";
import { Card } from "@/components/ui/card";
import { ErrorState, LoadingState } from "@/components/ui/states";
import type { Product } from "@/types";

export default function EditProductPage() {
  const id = useRouteParam("/products/[id]/edit", "id");
  const [product, setProduct] = useState<Product | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<Product>(`/api/products/${id}`)
      .then(setProduct)
      .catch((err) => setError(err instanceof Error ? err.message : "Could not load the product."));
  }, [id]);

  return (
    <>
      <PageHeader title="Edit item" />
      {error ? (
        <Card>
          <ErrorState message={error} />
        </Card>
      ) : !product ? (
        <Card>
          <LoadingState />
        </Card>
      ) : (
        <ProductForm product={product} />
      )}
    </>
  );
}
