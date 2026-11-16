"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function ImportForm() {
  const router = useRouter();
  const [msg, setMsg] = useState<{ ok: boolean; text: string; errors?: string[] } | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setBusy(true);
    setMsg(null);
    try {
      const res = await fetch("/api/admin/import", { method: "POST", body: new FormData(e.currentTarget) });
      const data = await res.json();
      if (!res.ok) setMsg({ ok: false, text: data.error, errors: data.errors });
      else {
        setMsg({ ok: true, text: `Importados ${data.total} productos: ${data.created} nuevos y ${data.updated} actualizados.`, errors: data.errors });
        router.refresh();
      }
    } catch {
      setMsg({ ok: false, text: "No se pudo subir el archivo" });
    }
    setBusy(false);
  }

  return (
    <form onSubmit={onSubmit} className="space-y-3 text-sm">
      <input type="file" name="file" accept=".xlsx,.csv" required className="block w-full text-sm file:mr-3 file:rounded-lg file:border-0 file:bg-tinta file:px-3 file:py-2 file:text-white" />
      <button disabled={busy} className="w-full rounded-lg bg-azul py-2 font-semibold text-white hover:bg-azul-oscuro disabled:bg-linea">
        {busy ? "Importando…" : "Importar catálogo"}
      </button>
      {msg && (
        <div className={`rounded-lg p-3 ${msg.ok ? "bg-emerald-50 text-emerald-800" : "bg-red-50 text-red-700"}`}>
          <p>{msg.text}</p>
          {msg.errors && msg.errors.length > 0 && (
            <ul className="mt-2 list-disc pl-4 text-xs">
              {msg.errors.map((e) => <li key={e}>{e}</li>)}
            </ul>
          )}
        </div>
      )}
    </form>
  );
}
