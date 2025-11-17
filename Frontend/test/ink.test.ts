import { InkLanguage } from "../src/lezer_ink"
import { testTree} from "@lezer/generator/dist/test"
import * as fs from "fs"
import * as path from "path"
import { fileURLToPath } from 'url';
import { it } from "vitest"

let caseDir = path.dirname(fileURLToPath(import.meta.url))

// lezer provide a `fileTests` helper which allows case
// files containing both the test input and the expected
// output. Unfortunately, the syntax used in the helper
// for separating input and expectation clashes badly with
// Ink syntax.
// So we have our own...
// #TODO: Turn this into a snapshot test to allow larger
// examples easily (see https://vitest.dev/guide/snapshot.html#custom-serializer)
for (let file of fs.readdirSync(caseDir)) {
  if (!/\.ink$/.test(file)) continue

  let name = /^[^\.]*/.exec(file)![0]
  it("Ink file: " + name, () => {
    const ink = fs.readFileSync(path.join(caseDir, file), "utf8")
    const expected = fs.readFileSync(path.join(caseDir, file + ".expect"), "utf8")
    const parsed = InkLanguage.parser.parse(ink)
    testTree(parsed, expected)
  })
}
