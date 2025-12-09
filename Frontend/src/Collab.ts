import * as signalR from "@microsoft/signalr"
import { Update, receiveUpdates, sendableUpdates, collab, getSyncedVersion, rebaseUpdates } from "@codemirror/collab"
import { EditorView, ViewPlugin, ViewUpdate, Tooltip, showTooltip } from "@codemirror/view"
import { EditorState, StateField, ChangeSet, Text, StateEffect } from "@codemirror/state"

const _connection: signalR.HubConnection | null = null

export let collabGroupName = ""

export const getConnection = () => _connection === null ? new signalR.HubConnectionBuilder().withUrl("/collab").build() : _connection

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
    } catch(e) {
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
    if(Object.keys(tooltipInfo).length === 1) {
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
        dom.textContent = tipTag
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
    sharedEffects: tr => tr.effects.filter(e => e.is(cursorEffect))
  }), plugin, cursorExtension(tag)]
}

type SerializedUpdate = { clientID: string, changes: unknown, effects: ToolTipInfo[] }

const serializeUpdate = (update: Update): SerializedUpdate => {
  return { clientID: update.clientID, changes: update.changes.toJSON(), effects: (update.effects ?? []).filter(effect => effect.is(cursorEffect)).map(effect => effect.value) }
}

const deserializeUpdate = (json: { clientID: string, changes: unknown, effects: ToolTipInfo[] }): Update => {
  return {
    clientID: json.clientID,
    changes: ChangeSet.fromJSON(json.changes),
    effects: json.effects.map(tti => cursorEffect.of(tti))
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
    await connection.send("DocumentRequested", connectionId, { version: updates.length, doc: doc.toString(), tag })
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
