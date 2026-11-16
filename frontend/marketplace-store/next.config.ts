import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // exceljs y pg se ejecutan solo en el servidor (Node.js).
  serverExternalPackages: ["exceljs", "pg", "@electric-sql/pglite"],
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
