import * as signalR from "@microsoft/signalr"
import { Update, receiveUpdates, sendableUpdates, collab, getSyncedVersion, rebaseUpdates } from "@codemirror/collab"
import { EditorView, ViewPlugin, ViewUpdate, Tooltip, showTooltip, Panel, showPanel } from "@codemirror/view"
import { EditorState, StateField, ChangeSet, Text, StateEffect } from "@codemirror/state"
import htmx from 'htmx.org';

const _connection: signalR.HubConnection | null = null

export let collabGroupName = ""

export const getConnection = () => _connection === null ? new signalR.HubConnectionBuilder().withUrl("/collab").build() : _connection

const setAttributes = (element: HTMLElement, attributes: Record<string, string>) => {
  Object.entries(attributes).forEach(([name, value]) => element.setAttribute(name, value))
}

export const titleEffect = StateEffect.define<string>()

export type ControlExtensionConfig = {
  startingTitle: string,
  scriptId: string | null,
  publishedUrl: string | null,
  token: {
    header: string,
    value: string
  }
}

export const controlExtension = ({ startingTitle, scriptId, publishedUrl, token }: ControlExtensionConfig) => {
  const speakerInfo = JSON.parse(document.getElementById("speaker-json")?.innerText ?? "[]")
  const sceneInfo = JSON.parse(document.getElementById("scene-json")?.innerText ?? "[]")
  const musicInfo = JSON.parse(document.getElementById("music-json")?.innerText ?? "[]")
  const titleField = StateField.define<string>({
    create() {
      return startingTitle
    },
    update(title, tr) {
      let latestTitle = tr.effects.filter(effect => effect.is(titleEffect)).slice(-1)[0]
      return latestTitle ? latestTitle.value : title
    }
  })
  function controlPanel(view: EditorView): Panel {
    let dom = document.createElement("div")
    dom.className = "buttons is-centered are-small mt-1 mb-1"

    if (scriptId === null) {
      let runButton = document.createElement("button")
      setAttributes(runButton, {
        id: "run-button",
        disabled: "",
        type: "button"
      })
      runButton.addEventListener(
        "click",
        () => {
          const ink = view.state.doc.toString()
          const title = view.state.field(titleField)
          localStorage.setItem('ink', ink)
          localStorage.setItem('title', title)

          htmx.ajax('post', "/playground/playthrough", {
            target: "#page",
            select: "#page",
            values: { ink },
            headers: { [token.header]: token.value }
          })
        }
      )
      runButton.innerText = "Test your script"
      runButton.className = "button is-primary"
      dom.appendChild(runButton)
    } else {
      let runButton = document.createElement("button")
      setAttributes(runButton, {
        id: "run-button",
        disabled: "",
        type: "button"
      })
      runButton.addEventListener(
        "click",
        () => {
          htmx.ajax('post', "/playthrough/start", {
            target: "#page",
            select: "#page",
            values: { script: scriptId },
            headers: { [token.header]: token.value }
          })
        }
      )
      runButton.innerText = "Run"
      runButton.className = "button is-primary"
      dom.appendChild(runButton)

      if (publishedUrl === null) {
        let publish = document.createElement("button")
        setAttributes(publish, {
          id: "publish",
          type: "button"
        })
        publish.addEventListener(
          "click",
          () => {
            htmx.ajax('post', "/script/publish", {
              target: "#page",
              select: "#page",
              values: { publish: scriptId },
              headers: { [token.header]: token.value }
            })
          }
        )
        publish.innerText = "Publish"
        publish.className = "button is-danger"
        dom.appendChild(publish)
      } else {
        let unpublish = document.createElement("button")
        setAttributes(unpublish, {
          id: "unpublish",
          type: "button",
        })
        unpublish.addEventListener(
          "click",
          () => {
            htmx.ajax('post', "/script/unpublish", {
              target: "#page",
              select: "#page",
              values: { unpublish: scriptId },
              headers: { [token.header]: token.value }
            })
          }
        )
        unpublish.innerText = "Unpublish"
        unpublish.className = "button is-danger"
        dom.appendChild(unpublish)
      }
    }

    let liveInvite = document.createElement("button")
    setAttributes(liveInvite, {
      type: "button"
    })
    liveInvite.addEventListener("click",
      async function getCollaborationLink() {
        const type = "text/plain";
        const text = `${document.location.origin}/script-collab/${collabGroupName}`
        const clipboardItemData = {
          [type]: text,
        };
        const clipboardItem = new ClipboardItem(clipboardItemData);
        await navigator.clipboard.write([clipboardItem]);
      })
    liveInvite.className = "button is-info"
    liveInvite.innerText = "Copy edit invite"
    dom.appendChild(liveInvite)

    return {
      dom
    }

  }

  function titlePanel(view: EditorView): Panel {
    let dom = document.createElement("div")
    dom.className = "field"
    let titleControl = document.createElement("div")
    titleControl.className = "control"
    let titleInput = document.createElement("input")
    let startingTitle = view.state.field(titleField)
    setAttributes(titleInput, {
      type: "text",
      name: "title",
      id: "story-title",
      value: startingTitle
    })
    titleInput.className = "input"
    titleInput.addEventListener("keyup", () => view.dispatch({ changes: [], effects: titleEffect.of(titleInput.value) }))
    titleInput.addEventListener("blur", () => view.dispatch({ changes: [], effects: titleEffect.of(titleInput.value) }))
    titleControl.appendChild(titleInput)
    dom.appendChild(titleControl)

    if (publishedUrl !== null) {
      let publishedWarning = document.createElement("p")
      let link = document.createElement("a")
      setAttributes(link, {
        href: publishedUrl
      })
      link.innerText = publishedUrl
      publishedWarning.className = "help is-danger"
      publishedWarning.append("Published at ")
      publishedWarning.append(link)
      dom.appendChild(publishedWarning)
    }

    let helperButtons = document.createElement("div")
    helperButtons.className = "mt-1 mb-1 buttons is-centered are-small"

    let speakerChooser = document.createElement("dialog")
    setAttributes(speakerChooser, { closedby: "any" })
    let speakerChooserButtons = document.createElement("div")
    speakerChooserButtons.className = "box buttons"
    for (const speaker of speakerInfo) {
      let button = document.createElement("button")
      setAttributes(button, { type: "button" })
      button.className = "button"
      button.innerText = speaker.name
      button.addEventListener("click", () => {
        const changes = view.state.changeByRange((range) => {
          const line = view.state.doc.lineAt(range.to)
          return { range, changes: [{ from: line.to, insert: `\n~speaker = "${speaker.name}"` }] }
        })
        view.dispatch(changes)
        speakerChooser.close()
      })
      speakerChooserButtons.appendChild(button)
    }
    speakerChooser.appendChild(speakerChooserButtons)
    dom.appendChild(speakerChooser)

    let changeSpeaker = document.createElement("button")
    setAttributes(changeSpeaker, { type: "button" })
    changeSpeaker.className = "button"
    changeSpeaker.addEventListener("click", () => {
      speakerChooser.showModal()
    })
    changeSpeaker.appendChild(makeIcon("account"))
    changeSpeaker.appendChild(makeSpan("Change speaker"))
    helperButtons.appendChild(changeSpeaker)

    let sceneChooser = document.createElement("dialog")
    setAttributes(sceneChooser, { closedby: "any" })
    let sceneChooserButtons = document.createElement("div")
    sceneChooserButtons.className = "box buttons"
    const makeSceneButton = (text: string) => {
      let button = document.createElement("button")
      setAttributes(button, { type: "button" })
      button.className = "button"
      button.innerText = text
      button.addEventListener("click", () => {
        const changes = view.state.changeByRange((range) => {
          const line = view.state.doc.lineAt(range.to)
          return { range, changes: [{ from: line.to, insert: `\n~scene = "${text}"` }] }
        })
        view.dispatch(changes)
        sceneChooser.close()
      })
      sceneChooserButtons.appendChild(button)
    }
    for (const scene of sceneInfo) {
      makeSceneButton(scene.name)
      for (const tag of scene.tags) {
        makeSceneButton(scene.name + " " + tag.tag)
      }
    }
    sceneChooser.appendChild(sceneChooserButtons)
    dom.appendChild(sceneChooser)

    let changeScene = document.createElement("button")
    setAttributes(changeScene, { type: "button" })
    changeScene.className = "button"
    changeScene.addEventListener("click", () => {
      sceneChooser.showModal()
    })
    changeScene.appendChild(makeIcon("home"))
    changeScene.appendChild(makeSpan("Change scene"))
    helperButtons.appendChild(changeScene)

    let musicChooser = document.createElement("dialog")
    setAttributes(musicChooser, { closedby: "any" })
    let musicChooserButtons = document.createElement("div")
    musicChooserButtons.className = "box buttons"

    for (const music of musicInfo) {
      let button = document.createElement("button")
      setAttributes(button, { type: "button" })
      button.className = "button"
      button.innerText = music.name
      button.addEventListener("click", () => {
        const changes = view.state.changeByRange((range) => {
          const line = view.state.doc.lineAt(range.to)
          return { range, changes: [{ from: line.to, insert: `\n~music = "${music.name}"` }] }
        })
        view.dispatch(changes)
        musicChooser.close()
      })
      musicChooserButtons.appendChild(button)
    }
    musicChooser.appendChild(musicChooserButtons)
    dom.appendChild(musicChooser)

    let changeMusic = document.createElement("button")
    setAttributes(changeMusic, { type: "button" })
    changeMusic.className = "button"
    changeMusic.addEventListener("click", () => {
      musicChooser.showModal()
    })
    changeMusic.appendChild(makeIcon("music")) 
    changeMusic.appendChild(makeSpan("Change music")) 
    helperButtons.appendChild(changeMusic)

    dom.appendChild(helperButtons)

    return {
      dom,
      top: true,
      update: (update) => {
        const latestTitleChange = update.transactions.flatMap(t => t.effects).filter((e) => e.is(titleEffect)).slice(-1)[0]
        if (latestTitleChange && latestTitleChange.value !== titleInput.value) {
          titleInput.value = latestTitleChange.value
        }
      }
    }
  }
  return [titleField, showPanel.of(titlePanel), showPanel.of(controlPanel)]
}

type ToolTipInfo = Record<string, number[]>

const cursorEffect = StateEffect.define<ToolTipInfo>()

const cursorExtension = (tag: string) => {
  const cursorTransactionExtender = EditorState.transactionExtender.of((tr) => {
    try {
      if (!tr.docChanged && !tr.selection) { return null }
      let ourTooltips = getCursorPositions(tr.state)
      let existingTooltips = tr.state.field(cursorTooltipField)
      let updated = Object.fromEntries(
        Object.entries(existingTooltips).map(
          ([tag, positions]) => [tag, positions.map(pos => tr.changes.mapPos(pos))])
      )
      return { effects: cursorEffect.of({ ...updated, [tag]: ourTooltips }) }
    } catch (e) {
      // If anything weird happens, ignore the effects
      console.warn("Cursor weirdness")
      return {}
    }
  })

  const cursorTooltipField = StateField.define<ToolTipInfo>({
    create(editorState) {
      return { [tag]: getCursorPositions(editorState) }
    },

    update(tooltips, tr) {
      return tr.effects.filter(effect => effect.is(cursorEffect)).reduce((acc, next) => ({ ...acc, ...next.value }), tooltips)
    },

    provide: f => showTooltip.computeN([f], state => makeCursorTooltips(state.field(f)))
  })

  function getCursorPositions(state: EditorState): number[] {
    return state.selection.ranges
      .filter(range => range.empty)
      .map(range => range.head)
  }

  function makeCursorTooltips(tooltipInfo: ToolTipInfo): Tooltip[] {
    // Don't show until there's somebody who isn't us
    if (Object.keys(tooltipInfo).length === 1) {
      return []
    }
    return Object.entries(tooltipInfo).flatMap(([tipTag, positions]) => positions.map(pos => ({
      pos: pos,
      above: true,
      strictSide: true,
      arrow: true,
      create: () => {
        let dom = document.createElement("div")
        dom.className = tipTag === tag ? "cm-tooltip-cursor-mine" : "cm-tooltip-cursor"
        dom.textContent = tipTag === tag ? `${tipTag} (me)` : tipTag
        return { dom }
      }
    })))
  }

  const cursorTooltipBaseTheme = EditorView.baseTheme({
    ".cm-tooltip.cm-tooltip-cursor": {
      backgroundColor: "#66b",
      color: "white",
      border: "none",
      padding: "2px 7px",
      borderRadius: "4px",
      "& .cm-tooltip-arrow:before": {
        borderTopColor: "#66b"
      },
      "& .cm-tooltip-arrow:after": {
        borderTopColor: "transparent"
      }
    },

    ".cm-tooltip.cm-tooltip-cursor-mine": {
      backgroundColor: "green",
      color: "white",
      border: "none",
      padding: "2px 7px",
      borderRadius: "4px",
      "& .cm-tooltip-arrow:before": {
        borderTopColor: "#66b"
      },
      "& .cm-tooltip-arrow:after": {
        borderTopColor: "transparent"
      }
    }
  })
  return [cursorTransactionExtender, cursorTooltipField, cursorTooltipBaseTheme]
}

export const makePeerExtension = (connection: signalR.HubConnection, groupName: string, tag: string, startVersion: number) => {
  collabGroupName = groupName
  let updates: (Update & { version: number })[] = []
  connection.on("UpdateBroadcast", (version: number, update: SerializedUpdate) => {
    console.log("Received broadcast")
    updates.push({ ...deserializeUpdate(update), version })
  })
  const pullUpdates = async (version: number) => {
    console.log("Pulling updates")
    const unseen = updates.filter(u => u.version >= version)
    updates = []
    if (unseen.length > 0) {
      return unseen
    } else {
      await new Promise((resolve) => connection.on("UpdateBroadcast", resolve))
      return pullUpdates(version)
    }
  }
  let plugin = ViewPlugin.fromClass(class {
    private pushing = false
    private done = false

    constructor(private view: EditorView) { this.pull() }

    update(update: ViewUpdate) {
      if (update.docChanged) this.push()
    }

    async push() {
      let updates = sendableUpdates(this.view.state)
      if (this.pushing || !updates.length) return
      this.pushing = true
      let version = getSyncedVersion(this.view.state)
      await connection.send("PushUpdates", groupName, version, updates.map(serializeUpdate))
      this.pushing = false
      // Regardless of whether the push failed or new updates came in
      // while it was running, try again if there's updates remaining
      if (sendableUpdates(this.view.state).length)
        setTimeout(() => this.push(), 300)
    }

    async pull() {
      while (!this.done) {
        let version = getSyncedVersion(this.view.state)
        let updates = await pullUpdates(version)

        this.view.dispatch(receiveUpdates(this.view.state, updates))
      }
    }

    destroy() { this.done = true }
  })
  return [collab({
    startVersion, clientID: groupName,
    sharedEffects: tr => tr.effects.filter(e => e.is(cursorEffect) || e.is(titleEffect))
  }), plugin, cursorExtension(tag)]
}

type SerializedSharedEffect = { _tag: "title", effect: string } | { _tag: "tooltip", effect: ToolTipInfo }
type SerializedUpdate = { clientID: string, changes: unknown, effects: SerializedSharedEffect[] }

const serializeUpdate = (update: Update): SerializedUpdate => {
  return {
    clientID: update.clientID,
    changes: update.changes.toJSON(),
    effects: (update.effects ?? [])
      .filter(effect => effect.is(cursorEffect) || effect.is(titleEffect))
      .map((effect): SerializedSharedEffect => effect.is(cursorEffect) ?
        { _tag: "tooltip", effect: effect.value } :
        { _tag: "title", effect: effect.value })
  }
}

const deserializeUpdate = (json: SerializedUpdate): Update => {
  return {
    clientID: json.clientID,
    changes: ChangeSet.fromJSON(json.changes),
    effects: json.effects.map(effect => effect._tag === "title" ? titleEffect.of(effect.effect) : cursorEffect.of(effect.effect))
  }
}

export const startCollabAuthority = (connection: signalR.HubConnection, groupName: string, startDocument: string) => {
  let doc = Text.of(startDocument.split('\n'))
  let updates: Update[] = []
  let peerCount = 1

  connection.on("RequestDocument", async (connectionId) => {
    console.log("Document requested, sending current state")
    let tag = peerCount
    peerCount++
    await connection.send("DocumentRequested", connectionId, { version: updates.length, doc: doc.toString(), tag: tag.toString() })
  })

  connection.on("PushUpdates", async (version, newUpdates: SerializedUpdate[]) => {
    console.log("Receiving and merging updates", version, newUpdates)
    let received: readonly Update[] = newUpdates.map(deserializeUpdate)
    if (version !== updates.length) {
      console.log("Rebasing updates")
      received = rebaseUpdates(received, updates.slice(version))
    }
    for (let update of received) {
      console.log("Pushing update")
      updates.push(update)
      doc = update.changes.apply(doc)
      console.log("Broadcasting accepted update")
      await connection.send("UpdateBroadcast", groupName, updates.length, serializeUpdate(update))
    }
  })
}

const makeIcon = (name:string) => {
  let span = document.createElement("span")
  span.className = "icon"
  let i = document.createElement("i")
  i.className = "mdi mdi-" + name
  span.appendChild(i)
  return span
}

const makeSpan = (text: string) => {
  let span = document.createElement("span")
  span.innerText = text
  return span
}
