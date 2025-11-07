import { InkLanguage } from "../src/lezer_ink"
import {fileTests, testTree} from "@lezer/generator/dist/test"
import * as fs from "fs"
import * as path from "path"
import { fileURLToPath } from 'url';
import { describe, it } from "vitest"

let caseDir = path.dirname(fileURLToPath(import.meta.url))

// lezer provide a `fileTests` helper which allows case
// files containing both the test input and the expected
// output. Unfortunately, the syntax used in the helper
// for separating input and expectation clashes badly with
// Ink syntax.
// So we have our own...
// #TODO: Turn this into a snapshot test to allow larger
// examples easily
for (let file of fs.readdirSync(caseDir)) {
  if (!/\.ink$/.test(file)) continue

  let name = /^[^\.]*/.exec(file)![0]
  it("Ink file: " + name, () => {
    const ink = fs.readFileSync(path.join(caseDir, file), "utf8")
    const expected = fs.readFileSync(path.join(caseDir, file + ".expect"), "utf8")
    testTree(InkLanguage.parser.parse(ink), expected)
  })
}

// Same for tags
describe("tags", () => {
  [
    { tags: "#tag", expected: "Script(Tag(TagName))" },
    { tags: "#tag #tag2", expected: "Script(Tag(TagName), Tag(TagName))" },
    { tags: "#tag blob", expected: "Script(Tag(TagName, TagValue))" },
    { tags: "-> goto #tag blob", expected: "Script(Divert(DivertArrow, Target), Tag(TagName, TagValue))" },
  ].forEach(({tags, expected }) => it(tags, () => testTree(InkLanguage.parser.parse(tags), expected)))
})
