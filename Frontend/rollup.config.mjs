import { nodeResolve }  from "@rollup/plugin-node-resolve"
import typescript from "@rollup/plugin-typescript"
import {lezer} from "@lezer/generator/rollup"

export default {
  input: 'frontend.ts',
  output: { file: '../wwwroot/bundle.js', format: 'iife', strict: false, name: 'fe' },
  plugins: [nodeResolve(), lezer(), typescript()]
}