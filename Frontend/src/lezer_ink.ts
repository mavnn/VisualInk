import { parser } from "./syntax.grammar"
import { LRLanguage, LanguageSupport, foldNodeProp } from "@codemirror/language"
import { styleTags, tags as t } from "@lezer/highlight"
import { SyntaxNode } from "@lezer/common"
import { EditorState } from "@codemirror/state"

function findKnotEnd(knotNode: SyntaxNode, state: EditorState) {
        let last: SyntaxNode = knotNode
        while (true) {
                let next = last.nextSibling
                if (!next) { break }
                last = next
                if (last.type.is("Knot")) { break }
        }
        return state.doc.lineAt(last.from).from - 1
}

export const InkLanguage = LRLanguage.define(
        {
                parser: parser.configure({
                        props: [
                                foldNodeProp.add({ Knot: (tree, state) => ({ from: state.doc.lineAt(tree.from).to, to: findKnotEnd(tree.node, state) }) }),
                                styleTags({
                                        Include: t.keyword,
                                        Todo: t.annotation,
                                        Identifier: t.className,
                                        Target: t.className,
                                        VariableDeclaration: t.keyword,
                                        VariableAssignment: t.keyword,
                                        VarValue: t.literal,
                                        LineComment: t.comment,
                                        BlockComment: t.blockComment,
                                        SectionMarker: t.keyword,
                                        DivertArrow: t.keyword,
                                        Knot: t.keyword,
                                        Stitch: t.keyword,
                                        ChoiceMarker: t.keyword,
                                        Tag: t.className,
                                        TagValue: t.literal,
                                        DynamicTypeMarker: t.keyword,
                                        Pipe: t.keyword,
                                        OrOp: t.keyword,
                                        AndOp: t.keyword,
                                        NotOp: t.keyword,
                                        Glue: t.className,
                                        function: t.keyword,
                                        "{ } < > =": t.keyword
                                })
                        ]
                }), languageData: { commentTokens: { line: "//", block: { open: "/*", close: "*/" } } }
        });

export const InkLanguageSupport = new LanguageSupport(InkLanguage)
