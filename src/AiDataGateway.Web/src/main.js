import { createApp } from 'vue'
import {
  ElAlert,
  ElConfigProvider,
  ElAvatar,
  ElButton,
  ElCard,
  ElCheckbox,
  ElCheckboxGroup,
  ElDatePicker,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElDivider,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMenu,
  ElMenuItem,
  ElOption,
  ElPagination,
  ElProgress,
  ElSelect,
  ElSubMenu,
  ElSwitch,
  ElTabPane,
  ElTable,
  ElTableColumn,
  ElTabs,
  ElTag,
  ElTimeSelect
} from 'element-plus'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import './styles.css'
import App from './App.vue'

if (localStorage.getItem('gateway.uiTheme') === 'dark') document.documentElement.classList.add('dark')

const app = createApp(App)

const components = [
  ElAlert,
  ElConfigProvider,
  ElAvatar,
  ElButton,
  ElCard,
  ElCheckbox,
  ElCheckboxGroup,
  ElDatePicker,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElDivider,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMenu,
  ElMenuItem,
  ElOption,
  ElPagination,
  ElProgress,
  ElSelect,
  ElSubMenu,
  ElSwitch,
  ElTabPane,
  ElTable,
  ElTableColumn,
  ElTabs,
  ElTag,
  ElTimeSelect
]

components.forEach(component => app.use(component))
for (const [name, component] of Object.entries(ElementPlusIconsVue)) app.component(name, component)
app.mount('#app')
