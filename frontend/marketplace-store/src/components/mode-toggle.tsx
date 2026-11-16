"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { setMode } from "@/app/actions";
import type { Channel } from "@/lib/shared";

export function ModeToggle({ mode }: { mode: Channel }) {
  const path = usePathname();
  const sp = useSearchParams();
  const back = path + (sp.size ? `?${sp.toString()}` : "");
  return (
    <form action={setMode} className="flex items-center gap-2 text-sm" aria-label="Tipo de cliente">
      <input type="hidden" name="back" value={back} />
      <span className="hidden text-white/70 lg:inline">Compro para</span>
      <div className="flex rounded-md bg-azul-oscuro p-0.5">
        {(["B2C", "B2B"] as const).map((m) => (
          <button
            key={m}
            name="modo"
            value={m}
            className={`rounded px-3 py-1.5 font-semibold transition-colors ${
              mode === m ? "bg-white text-azul" : "text-white/80 hover:text-white"
            }`}
            aria-pressed={mode === m}
          >
            {m === "B2C" ? "Mi casa" : "Mi empresa"}
          </button>
        ))}
      </div>
    </form>
  );
}
