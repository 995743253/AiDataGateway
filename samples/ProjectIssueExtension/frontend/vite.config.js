import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 构建产物输出到扩展的 wwwroot，随插件 ZIP 一起分发；base 使用相对路径，
// 兼容网关的 /custom-modules/{id}/ui/ 子路径挂载。
export default defineConfig({
  base: './',
  plugins: [vue()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true
  }
})
