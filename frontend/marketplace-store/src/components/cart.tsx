"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type { Channel } from "@/lib/shared";

export type CartItem = {
  sku: string;
  name: string;
  category: string;
  /** URL de la imagen del producto (opcional: carritos guardados antes no la tienen). */
  image?: string;
  price_b2c: number;
  price_b2b: number;
  stock: number;
  qty: number;
};

type CartCtx = {
  mode: Channel;
  items: CartItem[];
  ready: boolean;
  count: number;
  subtotal: number;
  priceOf: (i: Pick<CartItem, "price_b2c" | "price_b2b">) => number;
  add: (item: Omit<CartItem, "qty">, qty?: number) => void;
  setQty: (sku: string, qty: number) => void;
  remove: (sku: string) => void;
  clear: () => void;
};

const Ctx = createContext<CartCtx | null>(null);
const KEY = "logimkt-carrito";

export function CartProvider({ mode, children }: { mode: Channel; children: React.ReactNode }) {
  const [items, setItems] = useState<CartItem[]>([]);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    try {
      const saved = JSON.parse(localStorage.getItem(KEY) || "[]");
      // eslint-disable-next-line react-hooks/set-state-in-effect -- leer el carrito guardado al montar
      if (Array.isArray(saved)) setItems(saved);
    } catch {}
    setReady(true);
  }, []);

  useEffect(() => {
    if (ready) localStorage.setItem(KEY, JSON.stringify(items));
  }, [items, ready]);

  const priceOf = useCallback(
    (i: Pick<CartItem, "price_b2c" | "price_b2b">) => (mode === "B2B" ? i.price_b2b : i.price_b2c),
    [mode],
  );

  const value = useMemo<CartCtx>(() => {
    const add: CartCtx["add"] = (item, qty = 1) =>
      setItems((prev) => {
        const found = prev.find((p) => p.sku === item.sku);
        if (found) return prev.map((p) => (p.sku === item.sku ? { ...p, ...item, qty: Math.min(p.qty + qty, item.stock) } : p));
        return [...prev, { ...item, qty: Math.min(qty, item.stock) }];
      });
    return {
      mode,
      items,
      ready,
      count: items.reduce((s, i) => s + i.qty, 0),
      subtotal: Math.round(items.reduce((s, i) => s + priceOf(i) * i.qty, 0) * 100) / 100,
      priceOf,
      add,
      setQty: (sku, qty) =>
        setItems((prev) => prev.map((p) => (p.sku === sku ? { ...p, qty: Math.max(1, Math.min(qty, p.stock)) } : p))),
      remove: (sku) => setItems((prev) => prev.filter((p) => p.sku !== sku)),
      clear: () => setItems([]),
    };
  }, [items, ready, mode, priceOf]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useCart() {
  const c = useContext(Ctx);
  if (!c) throw new Error("useCart debe usarse dentro de CartProvider");
  return c;
}

export function CartCount() {
  const { count, ready } = useCart();
  if (!ready || count === 0) return null;
  return (
    <span className="absolute -right-2.5 -top-2 min-w-5 rounded-full bg-senal px-1.5 text-center text-xs font-bold leading-5 text-tinta">
      {count}
    </span>
  );
}

export function AddToCartButton({
  product,
  withQty = false,
  disabled = false,
  disabledLabel = "No disponible",
}: {
  product: Omit<CartItem, "qty">;
  withQty?: boolean;
  disabled?: boolean;
  disabledLabel?: string;
}) {
  const { add } = useCart();
  const [qty, setQty] = useState(1);
  const [done, setDone] = useState(false);
  const out = product.stock <= 0;
  const unavailable = out || disabled;
  return (
    <div className="flex items-center gap-2">
      {withQty && !unavailable && (
        <input
          type="number"
          min={1}
          max={product.stock}
          value={qty}
          onChange={(e) => setQty(Math.max(1, Math.min(product.stock, Number(e.target.value) || 1)))}
          className="w-20 rounded-md border border-linea bg-white px-3 py-2 text-sm"
          aria-label="Cantidad"
        />
      )}
      <button
        type="button"
        disabled={unavailable}
        onClick={() => {
          add(product, qty);
          setDone(true);
          setTimeout(() => setDone(false), 1500);
        }}
        className="flex-1 rounded-md bg-azul px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-azul-oscuro disabled:cursor-not-allowed disabled:bg-linea disabled:text-gris"
      >
        {out ? "Sin existencias" : disabled ? disabledLabel : done ? "Agregado al carrito" : "Agregar al carrito"}
      </button>
    </div>
  );
}
