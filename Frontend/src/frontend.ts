import htmx from 'htmx.org';
import 'idiomorph/htmx';
import { EditorView, basicSetup } from 'codemirror';
import { EditorState } from "@codemirror/state"
import { linter, lintGutter } from '@codemirror/lint'
import { CompletionContext, autocompletion, snippetCompletion } from "@codemirror/autocomplete"
import { InkLanguageSupport } from '@mavnn/codemirror-lang-ink'
import { InkElement } from './ink_element'
import { getConnection, makePeerExtension, collabGroupName, startCollabAuthority } from './Collab'

// @ts-ignore("Because we can!")
window.htmx = htmx;

customElements.define("ink-element", InkElement)

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

function tildeFromDashCompletions(context: CompletionContext) {
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

function tildeCompletions(context: CompletionContext) {
  let word = context.matchBefore(/~(\w*)/)
  if ((!word || word.from == word.to) && !context.explicit)
    return null
  return {
    from: word?.from ?? context.pos,
    options: [
      snippetCompletion('~speaker = "${name}"', { label: "~speaker", detail: "Change who is speaking" }),
      snippetCompletion('~scene = "${name}"', { label: "~scene", detail: "Change the background scene" }),
      snippetCompletion('~music = "${name}"', { label: "~music", detail: "Change the background music" }),
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

async function addEditor() {
  let doc = ""
  if (window.location.href.endsWith('demo')) {
    console.log('Trying to load stored demo script')
    let stored = localStorage.getItem('ink')
    if (stored) {
      doc = stored
    }
    let storedTitle = localStorage.getItem('title')
    let titleElement = document.getElementById('story-title')
    if (storedTitle && titleElement) {
      titleElement.setAttribute('value', storedTitle)
    }
  }
  if (doc.trim() === "") {
    doc = htmx.find('#last-saved')?.textContent!;
  }
  let [_, pathRoot, maybeGroupName] = document.location.pathname.split("/", 3)
  let peerExtension
  let connection = getConnection()
  await connection.start()
  if (pathRoot === "script-collab") {
    // We're using an invite link to collaborate
    console.log("Starting collaboration")
    let docRequested = new Promise<{ version: number, doc: string, tag: string }>(
      resolve => connection.on("DocumentRequested", resolve)
    )
    await connection.send("RequestDocument", maybeGroupName)
    const docState = await docRequested
    doc = docState.doc
    peerExtension = makePeerExtension(connection, maybeGroupName, docState.tag, docState.version)
  } else {
    await connection.send("CreateGroup")
    startCollabAuthority(connection, connection.connectionId!, doc)
    peerExtension = makePeerExtension(connection, connection.connectionId!, "0", 0)
  }

  editor = new EditorView({
    doc,
    extensions: [basicSetup,
      inkLinter,
      lintGutter(),
      peerExtension,
      autocompletion({
        override:
          [
            tildeFromDashCompletions,
            tildeCompletions,
            tagCompletions,
            sectionCompletions,
            inkCompletions
          ]
      }),
      changeListener,
      InkLanguageSupport],
  });

  htmx.find('#codemirror')?.replaceChildren(editor.dom);
}

function getEditor() {
  return editor;
}

function addExampleViewer() {
  let doc = htmx.find('#example-text')?.textContent!;
  let viewerBox = htmx.find('#example-viewer')!
  if (viewerBox.childNodes.length > 0) { return }

  new EditorView({
    parent: document.querySelector("#example-viewer")!,
    doc,
    extensions: [
      basicSetup,
      InkLanguageSupport,
      EditorState.readOnly.of(true),
      EditorView.editable.of(false),
      EditorView.contentAttributes.of({ tabindex: "0" })
    ]
  })
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
  let audio = document.getElementById('audio-background-music') as HTMLAudioElement
  if (audio) {
    startMusic(audio)
  }
  let sfx = document.getElementById('audio-sfx') as HTMLAudioElement
  if (sfx) {
    startMusic(sfx)
  }
  let exampleViewer = document.getElementById('example-viewer')
  if (exampleViewer) {
    addExampleViewer()
  }
})

async function getCollaborationLink() {
  const type = "text/plain";
  const text = `${document.location.origin}/script-collab/${collabGroupName}`
  const collabButton = document.getElementById("collab-button") as HTMLButtonElement
  collabButton.disabled = true
  const clipboardItemData = {
    [type]: text,
  };
  const clipboardItem = new ClipboardItem(clipboardItemData);
  await navigator.clipboard.write([clipboardItem]);
  collabButton.disabled = false
}

export { addEditor, getEditor, callLinter, requestFullscreenPlaythrough, addText, stopMusic, collabGroupName, getCollaborationLink };

