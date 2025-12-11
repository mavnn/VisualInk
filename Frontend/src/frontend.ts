import htmx from 'htmx.org';
import 'idiomorph/htmx';
import { InkEditor, InkElement } from './ink_element'
import { collabGroupName } from './Collab'

// @ts-ignore("Because we can!")
window.htmx = htmx;

customElements.define("ink-element", InkElement)
customElements.define("ink-editor", InkEditor)

function addText(text: string) {
  // let selection = editor?.state.selection.main!
  // editor?.dispatch({ changes: [{ from: selection.from, to: selection.to, insert: text }] })
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
})


export { requestFullscreenPlaythrough, addText, stopMusic, collabGroupName };

