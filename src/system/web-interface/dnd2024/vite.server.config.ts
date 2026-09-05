import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { measureJavaScriptBundle } from "./scripts/bundle-budget.mjs";

const projectRoot = fileURLToPath(new URL(".", import.meta.url));
export default defineConfig({
  base: "/ui/dnd2024-play/",
  root: resolve(projectRoot, "server-host"),
  publicDir: false,
  plugins: [react(), {
    name: "website-initial-javascript-budget",
    generateBundle(_options, bundle) {
      const report = measureJavaScriptBundle(bundle);
      this.info(`Initial JavaScript: ${report.initialGzipBytes} gzip bytes; all feature chunks: ${report.totalGzipBytes} gzip bytes.`);
      if (report.initialGzipBytes > 90_000) this.error("The website exceeds its 90 kB initial JavaScript budget.");
    },
  }],
  build: {
    emptyOutDir: true,
    outDir: resolve(projectRoot, "server-dist"),
    target: "es2022",
    rollupOptions: {
      input: resolve(projectRoot, "server-host", "index.html"),
    },
  },
});
