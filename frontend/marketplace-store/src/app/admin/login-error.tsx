"use client";

import { Suspense } from "react";
import { useSearchParams } from "next/navigation";

function Inner() {
  const e = useSearchParams().get("error");
  if (!e) return null;
  return (
    <p className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">
      {e === "config"
        ? "Falta configurar la variable ADMIN_PASSWORD en Vercel."
        : "Contraseña incorrecta."}
    </p>
  );
}

export function LoginErrorClient() {
  return (
    <Suspense>
      <Inner />
    </Suspense>
  );
}
