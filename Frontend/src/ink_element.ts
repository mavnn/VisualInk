import { EditorView, basicSetup } from 'codemirror';
import { InkLanguageSupport } from '@mavnn/codemirror-lang-ink'
import { EditorState } from "@codemirror/state"
import { syntaxHighlighting, defaultHighlightStyle, syntaxTree } from "@codemirror/language"
import { getConnection, makePeerExtension, startCollabAuthority, controlExtension, titleEffect, AssetInfo, activeCollabAuthority } from './Collab'
import { linter, lintGutter } from '@codemirror/lint'
import { CompletionContext, autocompletion, snippetCompletion } from "@codemirror/autocomplete"
import { SyntaxNode } from '@lezer/common'

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
    // DOM hasn't finished construction yet
    if (this.childElementCount === 0) {
      this.observer.observe(this, { childList: true })
    } else {
      this.addEditor()
    }
  }

  addEditor() {
    // It's possible for this to be triggered via a callback
    // even if the editor has already been drawn via the 
    // back button
    if (this.firstElementChild?.className.includes("cm-editor")) {
      return
    }
    console.log("Adding ink element")
    const text = this.textContent!.trim()
    const inkSupport =
      InkLanguageSupport({ dialect: "visualink" })
    const view = new EditorView({
      doc: text,
      extensions: [
        syntaxHighlighting(defaultHighlightStyle),
        inkSupport,
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

export class InkEditor extends HTMLElement {
  private observer;
  constructor() {
    super()
    console.log("Editor element constructed", this.childElementCount)
    this.addEditor = this.addEditor.bind(this)
    this.observer = new MutationObserver(this.addEditor)
  }

  // This callback can be called before child elements are
  // added, so all we do here is wait for the code to
  // be added if that's the case.
  connectedCallback() {
    console.log("Connected callback triggered", this.childElementCount)
    // DOM hasn't finished construction yet
    if (this.childElementCount === 0) {
      this.observer.observe(this, { childList: true })
    } else {
      this.addEditor()
    }
  }

  async addEditor() {
    const existing = [... this.children ?? []].find((c) => c.className.includes("cm-editor"))
    if (existing) {
      existing.remove()
    }
    console.log("Initializing editor", this.childElementCount)
    let doc = ""
    let title = this.getAttribute("title") ?? "New title"
    let tokenHeader = this.getAttribute("token-header") ?? ""
    let tokenValue = this.getAttribute("token-value") ?? ""
    let token = { header: tokenHeader, value: tokenValue }
    let scriptId = this.getAttribute("script-id")
    let publishedUrl = this.getAttribute("published-url")
    let speakerInfo = JSON.parse(document.getElementById("speaker-json")?.innerText ?? "[]")
    let sceneInfo = JSON.parse(document.getElementById("scene-json")?.innerText ?? "[]")
    let musicInfo = JSON.parse(document.getElementById("music-json")?.innerText ?? "[]")
    let assetInfo = { speakerInfo, sceneInfo, musicInfo }

    if (window.location.href.endsWith('demo')) {
      console.log('Trying to load stored demo script')
      let stored = localStorage.getItem('ink')
      if (stored) {
        doc = stored
      }

      const storedTitle = localStorage.getItem('title')
      if (storedTitle) { title = storedTitle }
    }
    if (doc.trim() === "") {
      const code = this.querySelector("pre")?.textContent ?? ""
      doc = code.trim()
    }
    let [_, pathRoot, maybeGroupName] = document.location.pathname.split("/", 3)
    let peerExtension
    let connection = getConnection()
    if (connection.state != "Connected") { await connection.start() }
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
      startCollabAuthority(connection, doc, scriptId!)
      const groupName = await activeCollabAuthority?.groupName!
      let docRequested = new Promise<{ version: number, doc: string, tag: string }>(
        resolve => connection.on("DocumentRequested", resolve)
      )
      await connection.send("RequestDocument", groupName)
      const docState = await docRequested
      doc = docState.doc
      console.log("Doc received, starting")
      peerExtension = makePeerExtension(connection, groupName, docState.tag, docState.version)
    }

    const editor = new EditorView({
      doc,
      extensions: [basicSetup,
        inkLinter(token),
        lintGutter(),
        peerExtension,
        autocompletion({
          override:
            [
              tildeFromDashCompletions,
              tagCompletions,
              sectionCompletions,
              inkCompletions,
              syntaxCompletions(assetInfo)
            ]
        }),
        changeListener(token),
        InkLanguageSupport({ dialect: "visualink" }),
        controlExtension({ startingTitle: title, scriptId, publishedUrl, token, assetInfo })
      ],
    });

    this.observer.disconnect()
    this.appendChild(editor.dom)
  }
}

var cachedAutocompleteContext: { lists: string[], globalVariables: string[], divertTargets: Record<string, string[]> } = { lists: [], globalVariables: [], divertTargets: { DONE: [], END: [] } }

const callLinter = (token: { header: string, value: string }) => async (view: EditorView) => {
  let title = (document.getElementById('story-title')! as HTMLInputElement).value;
  const response = await fetch('/script/lint',
    {
      method: 'POST',
      body: JSON.stringify({ title, ink: view.state.doc.toString() }),
      headers: { [token.header]: token.value }
    }
  );
  const { lines, autocompleteContext } = await response.json();
  if (autocompleteContext) { cachedAutocompleteContext = autocompleteContext }
  const runButton = document.getElementById('run-button');
  if (runButton) {
    lines.length === 0 ? runButton.removeAttribute('disabled') : runButton.setAttribute('disabled', "true");
  }
  return lines.map((lineBased: any) => {
    let line = view.state.doc.line(lineBased.line);
    return { message: lineBased.message, severity: lineBased.severity, from: line.from, to: line.to }
  })
}

const inkLinter = (token: { header: string, value: string }) => linter(callLinter(token))

const changeListener = (token: { header: string, value: string }) => EditorView.updateListener.of((v) => {
  const hasTitleChange = v.transactions.flatMap((t) => t.effects).find((e) => e.is(titleEffect))
  if (v.docChanged) {
    var runButton = document.getElementById('run-button');
    runButton?.setAttribute('disabled', "true");
  }
  if (!v.docChanged && hasTitleChange !== undefined) {
    callLinter(token)(v.view)
  }
})

function tildeFromDashCompletions(context: CompletionContext) {
  let word = context.matchBefore(/-(\w*)/)
  if ((!word || word.from == word.to) && !context.explicit)
    return null
  return {
    from: word?.from ?? context.pos,
    options: [
      { label: "---", type: "text", apply: "~", detail: "tilde" },
      ...cachedAutocompleteContext.globalVariables.map((v) => snippetCompletion(`~${v}`, { label: `~${v}` }))
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
      snippetCompletion('#emote ${name}', { label: "#emote" }),
      snippetCompletion('#sfx ${sound}', { label: "#sfx" }),
      snippetCompletion('#vfx shake', { label: "#vfx" }),
      snippetCompletion('#animation shake', { label: "#animation (use #vfx instead!)" })
    ]
  }
}

function sectionCompletions(context: CompletionContext) {
  let word = context.matchBefore(/^==+/)
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
  let word = context.matchBefore(/^\w*/)
  if ((!word || word.from == word.to) && !context.explicit)
    return null
  return {
    from: word?.from ?? context.pos,
    options: [
      snippetCompletion('VAR ${name} = ${value}', { label: "VAR", detail: "Add a variable to your script" }),
    ]
  }
}

function getKnotName(context: CompletionContext, syntaxNode: SyntaxNode) {
  if (syntaxNode.name == "Knot") {
    if (syntaxNode.firstChild) {
      return context.state.sliceDoc(syntaxNode.firstChild.from, syntaxNode.firstChild.to)
    }
  } else if (syntaxNode.parent) {
    return getKnotName(context, syntaxNode.parent)
  } else {
    return null
  }
}

const syntaxCompletions = (assetInfo: AssetInfo) => (context: CompletionContext) => {
  let nodeBefore = syntaxTree(context.state).resolveInner(context.pos, - 1)
  console.log(nodeBefore)
  if (nodeBefore.name == "DivertArrow") {
    const knotName = getKnotName(context, nodeBefore)
    const options = Object.entries(cachedAutocompleteContext.divertTargets).flatMap(([knot, stitches]) => [{ label: "-> " + knot }, ...stitches.map((stitch) => ({ label: "-> " + (knot === knotName ? stitch : `${knot}.${stitch}`) }))])
    return {
      from: nodeBefore.from,
      options,
      validFor: /->\W*\w*/
    }
  } else if (nodeBefore.name == "VariableAssignment") {
    const options = cachedAutocompleteContext.globalVariables.map((variable) => ({ label: "~" + variable }))
    return {
      from: nodeBefore.from,
      options,
      validFor: /~\w*/
    }
  } else if (nodeBefore.name == "String") {
    // is this string being assigned to one of our magic variables?
    const parent = nodeBefore.parent
    if(!parent || parent.name !== "VariableAssignment") {
      return null
    }
    const identifier = parent.firstChild ? { name: context.state.sliceDoc(parent.firstChild.from, parent.firstChild.to), endOfIndentifier: parent.firstChild.to } : null
    if (identifier === null) {
      return null
    }
    if (identifier.name === "speaker") {
      const options = assetInfo.speakerInfo.map((s) => ({ label: s.name }))
      return {
        from: nodeBefore.from + 1,
        options,
        validFor: /~\w*/
      }
    }
    if (identifier.name === "scene") {
      const options = assetInfo.sceneInfo.flatMap((s) => [{ label: s.name }, ...s.tags.map((t) => ({ label: `${s.name} ${t.tag}`}))])
      return {
        from: nodeBefore.from + 1,
        options,
        validFor: /~\w*/
      }
    }
    if (identifier.name === "music") {
      const options = assetInfo.musicInfo.map((s) => ({ label: s.name }))
      return {
        from: nodeBefore.from + 1,
        options,
        validFor: /~\w*/
      }
    }
    return null
  }
  else { return null }
}
