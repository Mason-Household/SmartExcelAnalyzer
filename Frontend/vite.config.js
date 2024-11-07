import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");

  return {
    plugins: [react()],
    server: {
      port: 3000,
      host: "0.0.0.0",
      strictPort: true,
      proxy: {
        "/api": {
          target: "http://localhost:5001",
          changeOrigin: true,
          secure: false,
          rewrite: (path) => path.replace(/^\/api/, ""),
        },
        "/progressHub": {
          secure: false,
          changeOrigin: true,
          target: env.VITE_SIGNALR_HUB_URL,
        },
      },
    },
    build: {
      outDir: "dist",
      sourcemap: true,
    },
  };
});
