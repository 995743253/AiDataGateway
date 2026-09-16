import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import App from './App.vue'
import './theme.css'

const app = createApp(App)
app.use(ElementPlus, { locale: zhCn })

function showErrorBox(message) {
  let box = document.getElementById('errbox')
  if (!box) {
    box = document.createElement('pre')
    box.id = 'errbox'
    box.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:99999;background:#fff0f0;color:#bd3e3e;padding:10px;font-size:12px;white-space:pre-wrap;max-height:45vh;overflow:auto;margin:0'
    document.body.appendChild(box)
  }
  box.textContent += String(message) + '\n'
}
window.addEventListener('error', event => showErrorBox(event.message))
window.addEventListener('unhandledrejection', event => showErrorBox('rejection: ' + (event.reason?.message || event.reason)))
app.config.errorHandler = (err) => showErrorBox(err?.stack || String(err))

app.mount('#app')

