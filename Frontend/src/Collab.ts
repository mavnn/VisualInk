import * as signalR from "@microsoft/signalr"
import { Update, receiveUpdates, sendableUpdates, collab, getSyncedVersion, rebaseUpdates } from "@codemirror/collab"
import { EditorView, ViewPlugin, ViewUpdate, Tooltip, showTooltip, Panel, showPanel } from "@codemirror/view"
import { EditorState, StateField, ChangeSet, Text, StateEffect } from "@codemirror/state"
import htmx from 'htmx.org';
import { buildDom, TaggenElement } from "./taggen";

const _connection: signalR.HubConnection | null = null

export let collabGroupName = ""

export const getConnection = () => _connection === null ? new signalR.HubConnectionBuilder().withUrl("/collab").build() : _connection

export const titleEffect = StateEffect.define<string>()

export type AssetInfo = {
  speakerInfo: { name: string, emotes: { emote: string }[] }[],
  sceneInfo: { name: string, tags: { tag: string }[] }[],
  musicInfo: { name: string }[]
}

export type ControlExtensionConfig = {
  startingTitle: string,
  scriptId: string | null,
  publishedUrl: string | null,
  token: {
    header: string,
    value: string
  },
  assetInfo: AssetInfo
}

const makeChooser = <T>(view: EditorView, input: { id: string, data: T[], map: (t: T) => { text: string, insert: string } }) => {
  const close = {
    tag: "button",
    className: "delete is-small",
    handlers: [
      [
        "click", () =>
          (htmx.find(`#${input.id}`) as HTMLDialogElement).close()
      ]]
  }
  const buttons =
  {
    tag: "div",
    className: "buttons pr-4",
    children:
      input.data.map((d) => {
        const { text, insert } = input.map(d)
        return {
          tag: "button",
          attributes: { type: "button" },
          className: "button",
          children: [text],
          handlers: [
            [
              "click",
              () => {
                const changes = view.state.changeByRange((range) => {
                  const line = view.state.doc.lineAt(range.to)
                  return { range, changes: [{ from: line.to, insert: insert }] }
                })
                view.dispatch(changes);
                (htmx.find(`#${input.id}`) as HTMLDialogElement).close()
              }
            ]
          ]
        }
      })
  }
  return {
    tag: "dialog",
    attributes: { closedby: "any", id: input.id },
    children: [
      {
        tag: "div",
        className: "notification",
        children: [close, buttons]
      }]
  }
}

export const controlExtension = ({ startingTitle, scriptId, publishedUrl, token, assetInfo }: ControlExtensionConfig) => {

  const { speakerInfo, sceneInfo, musicInfo } = assetInfo
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
    let nonownerActions: TaggenElement[] =
      [{
        tag: "button",
        className: "button is-primary",
        attributes: {
          id: "run-button",
          disabled: "",
          type: "button"
        },
        handlers:
          [[
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
          ]],
        children: ["Test run"]

      }]
    let ownerActions: TaggenElement[] = [
      {
        tag: "button",
        attributes: {
          id: "run-button",
          disabled: "",
          type: "button"
        },
        className: "button is-primary",
        children: ["Run"],
        handlers: [
          [

            "click",
            () => {
              htmx.ajax('post', "/playthrough/start", {
                target: "#page",
                select: "#page",
                values: { script: scriptId },
                headers: { [token.header]: token.value }
              })
            }
          ]
        ]
      },
      {
        tag: "button",
        attributes: {
          id: publishedUrl ? "unpublish" : "publish",
          type: "button"
        },
        className: "button is-danger",
        children: [publishedUrl ? "Unpublish" : "Publish"],
        handlers: [
          [

            "click",
            () => {
              htmx.ajax('post', `/script/${publishedUrl ? "unpublish" : "publish"}`, {
                target: "#page",
                select: "#page",
                values: { [publishedUrl ? "unpublish" : "publish"]: scriptId },
                headers: { [token.header]: token.value }
              })
            }
          ]
        ]
      }
    ]
    let dom: TaggenElement = {
      tag: "div",
      className: "buttons is-centered are-small mt-1 mb-1",
      children: [
        ...(scriptId === null ? nonownerActions : ownerActions),
        {
          tag: "button",
          attributes: { type: "button" },
          className: "button is-info",
          children: ["Copy edit invite"],
          handlers: [
            [
              "click",
              async () => {
                const type = "text/plain";
                const text = `${document.location.origin}/script-collab/${collabGroupName}`
                const clipboardItemData = {
                  [type]: text,
                };
                const clipboardItem = new ClipboardItem(clipboardItemData);
                await navigator.clipboard.write([clipboardItem]);
              }
            ]
          ]

        }
      ]
    }

    return {
      dom: buildDom(dom)
    }

  }

  function titlePanel(view: EditorView): Panel {
    let dom: TaggenElement = {
      tag: "div",
      className: "field",
      children: [
        {
          tag: "div",
          className: "control",
          children: [
            {
              tag: "input",
              attributes: {
                type: "text",
                name: "title",
                id: "story-title",
                value: view.state.field(titleField)
              },
              className: "input",
              handlers: [
                ["keyup", (evt) => view.dispatch({ changes: [], effects: titleEffect.of((evt.target as HTMLInputElement).value) })],
                ["blur", (evt) => view.dispatch({ changes: [], effects: titleEffect.of((evt.target as HTMLInputElement).value) })],
              ]
            }
          ]
        },
        publishedUrl !== null ? {
          tag: "p",
          className: "help is-danger",
          children: [
            "Published at ",
            { tag: "a", attributes: { href: publishedUrl }, children: [publishedUrl] }
          ]
        } : "",
        {
          tag: "div",
          className: "mt-1 mb-1 buttons is-centered are-small",
          children: [
            {
              tag: "button",
              attributes: { type: "button" },
              handlers: [["click", (event) => htmx.toggleClass(htmx.find(".cm-lineNumbers")!, "hideLineNumbers")]],
              className: "button",
              children: [
                makeIcon("format-list-numbered"),
                makeSpan("Toggle line numbers")
              ]
            },
            {
              tag: "button",
              attributes: { type: "button" },
              className: "button",
              handlers: [[
                "click",
                () => (htmx.find("#speaker-chooser") as HTMLDialogElement).showModal()
              ]],
              children: [
                makeIcon("account"),
                makeSpan("Change speaker")
              ]
            },
            {
              tag: "button",
              attributes: { type: "button" },
              className: "button",
              handlers: [[
                "click",
                () => (htmx.find("#scene-chooser") as HTMLDialogElement).showModal()
              ]],
              children: [
                makeIcon("home"),
                makeSpan("Change scene")
              ]
            },
            {
              tag: "button",
              attributes: { type: "button" },
              className: "button",
              handlers: [[
                "click",
                () => (htmx.find("#music-chooser") as HTMLDialogElement).showModal()
              ]],
              children: [
                makeIcon("music"),
                makeSpan("Change music")
              ]
            },
          ]
        },
        // speaker-chooser
        makeChooser(view, {
          id: "speaker-chooser", data: [{ name: "Narrator", emotes: [] }, ...speakerInfo],
          map: (speaker) => ({ text: speaker.name, insert: `\n~speaker = "${speaker.name}"` })
        }),
        // scene-chooser
        makeChooser(view, {
          id: "scene-chooser",
          data: sceneInfo,
          map: (scene: { name: string }) => ({ text: scene.name, insert: `\n~scene = "${scene.name}"` })
        }),
        // music-chooser
        makeChooser(view, {
          id: "music-chooser",
          data: musicInfo,
          map: (music: { name: string }) => ({ text: music.name, insert: `\n~music = "${music.name}"` })
        }),
      ]
    }

    return {
      dom: buildDom(dom),
      top: true,
      update: (update) => {
        const latestTitleChange = update.transactions.flatMap(t => t.effects).filter((e) => e.is(titleEffect)).slice(-1)[0]
        const titleInput = htmx.find("#story-title") as HTMLInputElement
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

const makeIcon = (name: string) => {
  return {
    tag: "span",
    className: "icon",
    children: [
      { tag: "i", className: "mdi mdi-" + name }
    ]
  }
}

const makeSpan = (text: string) => {
  return {
    tag: "span",
    children: [text]
  }
}
