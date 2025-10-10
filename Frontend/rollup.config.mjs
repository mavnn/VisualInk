import { nodeResolve }  from "@rollup/plugin-node-resolve"

export default {
  input: 'frontend.js',
  output: { file: '../wwwroot/bundle.js', format: 'iife', strict: false, name: 'fe' },
  plugins: [nodeResolve()]
}