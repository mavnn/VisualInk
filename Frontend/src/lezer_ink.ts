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
                                        BlockComment: t.blockComment,
                                        AuthorWarning: t.comment,
                                        ContentLine: t.content,
                                        Glue: t.controlOperator,
                                        not: t.logicOperator,
                                        Name: t.literal,
                                        ListMember: t.literal,
                                        DivertArrow: t.controlOperator,
                                        Path: t.literal,
                                        END: t.keyword,
                                        DONE: t.keyword,
                                        Bool: t.bool,
                                        Int: t.number,
                                        Float: t.number,
                                        String: t.string,
                                        SequenceTypeMarker: t.operator,
                                        Pipe: t.controlKeyword,
                                        Tag: t.labelName,
                                        LineComment: t.comment,
                                        Include: t.keyword,
                                        VariableDeclaration: t.keyword,
                                        ConstDeclaration: t.keyword,
                                        ListDeclaration: t.keyword,
                                        ListDefinition: t.keyword,
                                        SelectedName: t.literal,
                                        VariableAssignment: t.keyword,
                                        Adjust: t.operator,
                                        IncDec: t.operator,
                                        OnceOnlyChoice: t.controlOperator,
                                        BracketName: t.literal,
                                        ChoiceCondition: t.controlOperator,
                                        WeaveBracket: t.controlOperator,
                                        ChoiceTag: t.labelName,
                                        RepeatingChoice: t.controlOperator,
                                        Knot: t.keyword,
                                        KnotArguments: t.bracket,
                                        ref: t.keyword,
                                        _function: t.keyword,
                                        Stitch: t.keyword,
                                        Gather: t.keyword,
                                        "InlineSequence BlockSequence": t.controlKeyword,
                                        SequenceContent: t.content,
                                        "{ } < > =": t.keyword
                                })
                        ]
                }), languageData: { commentTokens: { line: "//", block: { open: "/*", close: "*/" } } }
        });

export const InkLanguageSupport = new LanguageSupport(InkLanguage)
