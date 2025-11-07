import { defineConfig } from 'vitest/config'
import {lezer} from "@lezer/generator/rollup"

export default defineConfig({
  plugins: [lezer()]
})
