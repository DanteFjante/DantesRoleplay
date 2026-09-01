import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const projectRoot = fileURLToPath(new URL(".", import.meta.url));
export default defineConfig({
  base: "/ui/dnd2024-play/",
  root: resolve(projectRoot, "server-host"),
  publicDir: false,
  plugins: [react()],
  build: {
    emptyOutDir: true,
    outDir: resolve(projectRoot, "server-dist"),
    target: "es2022",
    rollupOptions: {
      input: resolve(projectRoot, "server-host", "index.html"),
    },
  },
});
