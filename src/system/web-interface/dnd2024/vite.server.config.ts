import { cpSync, mkdirSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const projectRoot = fileURLToPath(new URL(".", import.meta.url));
const reviewedMapAssets = [
  "city-map-crownmere-v2.png",
  "city-map-merrowgate-v2.png",
] as const;

export default defineConfig({
  base: "/ui/dnd2024-play/",
  root: resolve(projectRoot, "server-host"),
  publicDir: false,
  plugins: [
    react(),
    {
      name: "copy-reviewed-dnd2024-map-assets",
      closeBundle() {
        const outputAssets = resolve(projectRoot, "server-dist", "assets");
        mkdirSync(outputAssets, { recursive: true });
        for (const asset of reviewedMapAssets) {
          cpSync(resolve(projectRoot, "public", asset), resolve(outputAssets, asset));
        }
      },
    },
  ],
  build: {
    emptyOutDir: true,
    outDir: resolve(projectRoot, "server-dist"),
    target: "es2022",
    rollupOptions: {
      input: resolve(projectRoot, "server-host", "index.html"),
    },
  },
});
