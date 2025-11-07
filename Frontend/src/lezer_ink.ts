import { parser } from "./syntax.grammar"
import { LRLanguage, LanguageSupport } from "@codemirror/language"
import { styleTags, tags as t } from "@lezer/highlight"

export const InkLanguage = LRLanguage.define(
        {
                parser: parser.configure({
                        props: [
                                styleTags({
                                        Identifier: t.className,
                                        Target: t.className,
                                        VariableDeclaration: t.keyword,
                                        VariableAssignment: t.keyword,
                                        VarValue: t.literal,
                                        LineComment: t.comment,
                                        SectionMarker: t.keyword,
                                        DivertArrow: t.keyword,
                                        Knot: t.keyword,
                                        ChoiceMarker: t.keyword,
                                        "{ }": t.paren
                                })
                        ]
                }), languageData: { commentTokens: { line: "//" } }
        });

export const InkLanguageSupport = new LanguageSupport(InkLanguage)
