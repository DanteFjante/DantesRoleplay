import { cpSync, mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

import { buildBundledRulesCatalog } from "./src/server/bundled-rules-catalog";

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
        const entitiesRoot = resolve(projectRoot, "../../../..", "catalog/applications/dnd2024/content/entities");
        const rules = buildBundledRulesCatalog(entitiesRoot);
        writeFileSync(resolve(outputAssets, "rules-catalog.json"), JSON.stringify(rules), "utf8");
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
