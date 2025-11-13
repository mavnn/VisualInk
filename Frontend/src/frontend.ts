import htmx from 'htmx.org';
import 'idiomorph/htmx';
import { EditorView, basicSetup } from 'codemirror';
import { linter, lintGutter } from '@codemirror/lint'
import { CompletionContext, autocompletion, snippetCompletion } from "@codemirror/autocomplete"
import { InkLanguageSupport } from './lezer_ink'

// @ts-ignore("Because we can!")
window.htmx = htmx;

let editor: EditorView | null = null;

async function callLinter(view: EditorView) {
        let token = document.getElementById('editor')?.firstElementChild?.getAttribute('value')!;
        let title = (document.getElementById('story-title')! as HTMLInputElement).value;
        const response = await fetch('/script/lint',
                {
                        method: 'POST',
                        body: JSON.stringify({ title, ink: view.state.doc.toString() }),
                        headers: { RequestVerificationToken: token }
                }
        );
        const lines = await response.json();
        const runButton = document.getElementById('run-button');
        if (runButton) {
                lines.length === 0 ? runButton.removeAttribute('disabled') : runButton.setAttribute('disabled', "true");
        }
        return lines.map((lineBased: any) => {
                let line = view.state.doc.line(lineBased.line);
                return { message: lineBased.message, severity: lineBased.severity, from: line.from, to: line.to }
        })

}

const inkLinter = linter(callLinter)

function tildeCompletions(context: CompletionContext) {
        let word = context.matchBefore(/-(\w*)/)
        if ((!word || word.from == word.to) && !context.explicit)
                return null
        return {
                from: word?.from ?? context.pos,
                options: [
                        { label: "---", type: "text", apply: "~", detail: "tilde" },
                        snippetCompletion('~speaker = "${name}"', { label: "-speaker", detail: "Change who is speaking" }),
                        snippetCompletion('~scene = "${name}"', { label: "-scene", detail: "Change the background scene" }),
                        snippetCompletion('~music = "${name}"', { label: "-music", detail: "Change the background music" }),
                ]
        }
}

function tagCompletions(context: CompletionContext) {
        let word = context.matchBefore(/\#\w*/)
        if ((!word || word.from == word.to) && !context.explicit)
                return null
        return {
                from: word?.from ?? context.pos,
                options: [
                        snippetCompletion('#vo', { label: "#vo" }),
                        snippetCompletion('#emote ${name}', { label: "#emote" })
                ]
        }
}

function sectionCompletions(context: CompletionContext) {
        let word = context.matchBefore(/==+/)
        if ((!word || word.from == word.to) && !context.explicit)
                return null
        return {
                from: word?.from ?? context.pos,
                options: [
                        snippetCompletion('=== ${section} ===', { label: "===", detail: "Add a new named section to your script" }),
                ]
        }
}

function inkCompletions(context: CompletionContext) {
        let word = context.matchBefore(/\w*/)
        if ((!word || word.from == word.to) && !context.explicit)
                return null
        return {
                from: word?.from ?? context.pos,
                options: [
                        snippetCompletion('VAR ${name} = ${value}', { label: "VAR", detail: "Add a variable to your script" }),
                ]
        }
}

const changeListener = EditorView.updateListener.of((v) => {
        if (v.docChanged) {
                var runButton = document.getElementById('run-button');
                runButton?.setAttribute('disabled', "true");
        }
})

function addEditor() {
        let doc = htmx.find('#last-saved')?.textContent!;
        editor = new EditorView({
                doc,
                extensions: [basicSetup,
                        inkLinter,
                        lintGutter(),
                        autocompletion({ override: [tildeCompletions, tagCompletions, sectionCompletions, inkCompletions] }),
                        changeListener,
                        InkLanguageSupport],
        });

        htmx.find('#codemirror')?.replaceChildren(editor.dom);
}

function getEditor() {
        return editor;
}

function addText(text: string) {
        let selection = editor?.state.selection.main!
        editor?.dispatch({ changes: [{ from: selection.from, to: selection.to, insert: text }] })
}

function requestFullscreenPlaythrough() {
        if (document.fullscreenElement === null) {
                document.documentElement.requestFullscreen();
        }
}

function stopMusic(audioElement: HTMLAudioElement) {
        console.log("stopping music")
        audioElement.pause();
}

function startMusic(audioElement: HTMLAudioElement) {
        console.log("starting music")
        audioElement.play();
}

htmx.onLoad(function() {
        let audio = document.getElementById('audio-background-music')! as HTMLAudioElement
        if (audio) {
                startMusic(audio)
        }
})

export { addEditor, getEditor, callLinter, requestFullscreenPlaythrough, addText, stopMusic };

