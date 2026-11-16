// Código de barras EAN-13 real dibujado en SVG a partir del código EAN del catálogo.

const L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
const G = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
const R = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
const PARITY = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

function checksum(d12: string) {
  const s = d12.split("").reduce((acc, ch, i) => acc + Number(ch) * (i % 2 ? 3 : 1), 0);
  return String((10 - (s % 10)) % 10);
}

function encode(code: string): string | null {
  let digits = code.replace(/\D/g, "");
  if (digits.length === 12) digits += checksum(digits);
  if (digits.length !== 13) return null;
  const first = Number(digits[0]);
  let bits = "101";
  for (let i = 1; i <= 6; i++) bits += (PARITY[first][i - 1] === "L" ? L : G)[Number(digits[i])];
  bits += "01010";
  for (let i = 7; i <= 12; i++) bits += R[Number(digits[i])];
  bits += "101";
  return bits;
}

export function Barcode({ code, className = "", showText = true }: { code: string | null; className?: string; showText?: boolean }) {
  if (!code) return null;
  const bits = encode(code);
  if (!bits) return null;
  const guard = new Set([0, 1, 2, 45, 46, 47, 48, 49, 92, 93, 94]);
  return (
    <svg viewBox={`0 0 ${bits.length + 4} ${showText ? 32 : 26}`} className={className} role="img" aria-label={`Código EAN ${code}`}>
      {bits.split("").map((b, i) =>
        b === "1" ? <rect key={i} x={i + 2} y={0} width={1} height={guard.has(i) ? 26 : 22} fill="currentColor" /> : null,
      )}
      {showText && (
        <text x={(bits.length + 4) / 2} y={31.5} textAnchor="middle" fontSize={6} letterSpacing={1.2} fill="currentColor">
          {code}
        </text>
      )}
    </svg>
  );
}
