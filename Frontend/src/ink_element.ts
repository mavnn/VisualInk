import { EditorView, basicSetup } from 'codemirror';
import { InkLanguageSupport } from '@mavnn/codemirror-lang-ink'
import { EditorState } from "@codemirror/state"
import { syntaxHighlighting, defaultHighlightStyle } from "@codemirror/language"

export class InkElement extends HTMLElement {
        private observer;
        constructor() {
                super()
                this.addEditor = this.addEditor.bind(this)
                this.observer = new MutationObserver(this.addEditor)
        }

        // This callback can be called before child elements are
        // added, so all we do here is wait for the code to
        // be added if that's the case.
        connectedCallback() {
                if (this.childElementCount === 0) {
                        this.observer.observe(this, { childList: true })
                } else {
                        this.addEditor()
                }
        }

        addEditor() {
                const text = this.textContent!.trim()
                const view = new EditorView({
                        doc: text,
                        extensions: [
                                syntaxHighlighting(defaultHighlightStyle),
                                InkLanguageSupport,
                                EditorState.readOnly.of(true),
                                EditorView.editable.of(false)
                        ]

                })
                this.observer.disconnect()
                view.dom.classList.add("mb-2")
                view.dom.classList.add("mt-2")
                view.dom.classList.add("box")
                this.replaceChildren(view.dom)

        }

}
