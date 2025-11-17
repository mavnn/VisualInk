import { ExternalTokenizer } from "@lezer/lr"

// @ts-ignore-next "The terms file is generated during the grammar build process"
import { inlineConditionalOpen, inlineSequenceOpen, blockOpen } from "./generated/parser.terms"

const pipe = 124, colon = 58, braceL = 123, braceR = 125, newLine = 10, carriageReturn = 13

export const checkBrace = new ExternalTokenizer((input) => {
        let ahead = 0
        let peeked = input.peek(0)
        let nestedOpeningCount = 0
        while (peeked !== -1) {
                if (peeked === braceL) {
                        nestedOpeningCount++
                } else if (peeked === pipe) {
                        input.acceptToken(inlineSequenceOpen)
                        return
                } else if (peeked === braceR) {
                        if (nestedOpeningCount === 0) {
                                input.acceptToken(inlineConditionalOpen)
                                return
                        } else {
                                nestedOpeningCount--
                        }
                } else if (peeked === colon) {
                        ahead++
                        peeked = input.peek(ahead)
                        while (peeked !== -1) {
                                if (peeked === braceL) {
                                        nestedOpeningCount++
                                } else if (peeked === newLine) {
                                        input.acceptToken(blockOpen)
                                        return
                                } else if (peeked === braceR) {
                                        if (nestedOpeningCount === 0) {
                                                input.acceptToken(inlineConditionalOpen)
                                                return
                                        } else {
                                                nestedOpeningCount--
                                        }
                                }
                                ahead++
                                peeked = input.peek(ahead)
                        }
                } else if (peeked === newLine) {
                        return
                } else {
                        ahead++
                        peeked = input.peek(ahead)
                }
        }
}, { fallback: true })

