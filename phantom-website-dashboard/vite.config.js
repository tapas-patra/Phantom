import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 4173,
    proxy: {
      "/api/windows": {
        target: "http://localhost:5057",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api\/windows/, "")
      },
      "/api/dashboard-backend": {
        target: "http://localhost:5067",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api\/dashboard-backend/, "")
      }
    }
  }
});
