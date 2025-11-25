import { nodeResolve }  from "@rollup/plugin-node-resolve"
import typescript from "@rollup/plugin-typescript"

export default {
  input: 'src/frontend.ts',
  output: { file: '../wwwroot/bundle.js', format: 'iife', strict: false, name: 'fe' },
  plugins: [nodeResolve(), typescript()]
}