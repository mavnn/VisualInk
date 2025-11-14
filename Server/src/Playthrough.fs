module VisualInk.Server.Playthrough

open Marten
open Microsoft.Extensions.Logging
open Falco
open Falco.Markup
open Falco.Htmx
open Falco.Routing
open Microsoft.AspNetCore.Antiforgery
open System.Text.Json.Serialization
open System.Linq
open Prelude
open Script
open View

module B = Bulma

[<JsonFSharpConverter>]
type Choice = { index: int32; text: string }

[<JsonFSharpConverter>]
type RunAs =
  | RunAsWriter
  | RunAsPublisher of System.Guid

[<JsonFSharpConverter(SkippableOptionFields = SkippableOptionFields.Always)>]
type Step =
  { gameTitle: string
    scriptId: System.Guid
    speaker: string option
    show: bool
    emote: string option
    music: string option
    scene: string option
    text: string
    choices: Choice array
    finished: bool
    animation: string option
    stepCount: int
    runAs: RunAs }

[<JsonFSharpConverter>]
type Steps =
  { currentStep: Step
    previousStep: Step }

[<JsonFSharpConverter>]
type PlaythroughDocument =
  { id: System.Guid
    state: string
    script: Script
    steps: Steps }

let getStep previousStep (story: Ink.Runtime.Story) =
  handler {
    let speakerVariable =
      story.variablesState.["speaker"] :?> string

    let speaker =
      Option.ofObj speakerVariable
      |> Option.bind (fun v ->
        if v = "Narrator" || v = "" then None else Some v)

    let sceneVariable =
      story.variablesState.["scene"] :?> string

    let scene =
      Option.ofObj sceneVariable
      |> Option.bind (fun v ->
        if v = "" then None else Some v)

    let musicVariable =
      story.variablesState.["music"] :?> string

    let music =
      Option.ofObj musicVariable
      |> Option.bind (fun v ->
        if v = "" then None else Some v)

    let choices =
      story.currentChoices
      |> Seq.map (fun c ->
        { index = c.index; text = c.text })
      |> Seq.toArray

    let emote =
      story.currentTags
      |> Seq.tryFind (fun t -> t.StartsWith "emote ")
      |> Option.map (fun t -> t.Split(' ', 2).[1])

    let show = story.currentTags.Contains "vo" |> not

    let animation =
      story.currentTags
      |> Seq.tryFind (fun t -> t.StartsWith "animation ")
      |> Option.map (fun t -> t.Split(' ', 2).[1])

    return
      { previousStep with
          text = story.currentText
          speaker = speaker
          choices = choices
          scene = scene
          show = show
          music = music
          emote = emote
          animation = animation
          finished =
            not story.canContinue && choices.Length = 0
          stepCount = previousStep.stepCount + 1 }
  }

let make script =
  handler {
    let! logger =
      Handler.plug<ILogger<PlaythroughDocument>, _> ()

    let story = Ink.Runtime.Story script.inkJson
    story.add_onError (fun msg t -> logger.LogError(msg, t))

    story.add_onChoosePathString (fun str arr ->
      logger.LogInformation(str, arr))

    story.add_onDidContinue (fun () ->
      logger.LogInformation "Continued!")

    story.add_onMakeChoice (fun c ->
      logger.LogInformation(
        "Choice made {index}!",
        c.index
      ))

    story
  }

let load (guid: System.Guid) =
  handler {
    let! user = User.getSessionUser ()

    return!
      match user with
      | Some _ ->
        DocStore.load<PlaythroughDocument, _, _> guid
      | None ->
        DocStore.loadShared<PlaythroughDocument, _, _> guid
  }
  |> Handler.ofOption (
    Response.withStatusCode 404 >> Response.ofEmpty
  )

let private getRunnerSession () =
  User.getSessionUser ()
  |> Handler.bind (fun u ->
    match u with
    | Some _ -> DocStore.startSession ()
    | None -> DocStore.startSharedSession ())

let cont (guid: System.Guid) choice =
  handler {
    let! doc = load guid
    let! story = make doc.script
    story.state.LoadJson doc.state

    do
      match choice with
      | Some c -> story.ChooseChoiceIndex c
      | None -> ()

    story.Continue() |> ignore

    let previousStep = doc.steps.currentStep
    let! currentStep = getStep previousStep story

    let steps =
      { currentStep = currentStep
        previousStep = previousStep }

    use! session = getRunnerSession ()

    session.Update
      { doc with
          state = story.state.ToJson()
          steps = steps }

    do! DocStore.saveChanges session
    return steps
  }

let private start script runAs =
  handler {
    let guid = System.Guid.NewGuid()
    let! story = make script

    let state = story.state.ToJson()

    let step =
      { gameTitle = script.title
        scriptId = script.id
        text = ""
        scene = None
        music = None
        speaker = None
        emote = None
        show = false
        choices = [||]
        finished = false
        animation = None
        stepCount = 0
        runAs = runAs }

    use! session = getRunnerSession ()

    session.Insert
      { id = guid
        script = script
        state = state
        steps =
          { currentStep = step
            previousStep = step } }

    do! DocStore.saveChanges session

    let! _ = cont guid None

    return guid
  }

let private nextLink guid =
  $"/playthrough/{guid.ToString()}"

let private playthroughHxAttrs
  guid
  (token: AntiforgeryTokenSet)
  =
  [ Hx.post (nextLink guid)
    Hx.headers [ token.HeaderName, token.RequestToken ] ]

let private makeChoiceMenu guid step token =
  let choices =
    match step.finished, step.choices.Length with
    | false, 0 -> []
    | false, _ ->
      step.choices
      |> Seq.map (fun c ->
        _button
          [ yield! playthroughHxAttrs guid token
            _value_ (c.index.ToString())
            _name_ "choice"
            _class_ "button is-link is-fullwidth" ]
          [ _text c.text ])
      |> Seq.toList
    | true, _ ->
      let href =
          match step.runAs with
          | RunAsWriter -> $"/script/{step.scriptId.ToString()}"
          | RunAsPublisher _ -> "/"
      [ _a
          [ Hx.swapOuterHtml
            Hx.select "#page"
            Hx.targetCss "#page"
            _class_ "button is-link is-fullwidth"
            _href_ href ]
          [ _text "The End" ] ]

  _div
    [ _class_
        "container is-fluid is-flex is-align-items-end has-text-centered" ]
    [ _div
        [ _class_ "buttons mb-5"; _style_ "width: 100%;" ]
        choices ]

let private makeContentBox step =
  [ match step.speaker with
    | Some c ->
      yield
        _h3
          [ _class_ "subtitle has-text-primary fade"
            _id_ "character-name" ]
          [ _textEnc $"{c}:" ]
    | None -> ()
    yield
      _p
        [ _id_ "speech"; _class_ "fade" ]
        [ _textEnc step.text ] ]

let private makeAudio steps =
  handler {
    let! audio =
      match
        steps.currentStep.finished, steps.currentStep.music
      with
      | false, Some music ->
        match steps.currentStep.runAs with
        | RunAsWriter -> StoryAssets.findMusic music
        | RunAsPublisher guid ->
          StoryAssets.findMusicAs guid music
      | _ -> Handler.return' None

    let audioSrc =
      audio
      |> Option.map (fun a -> a.url)
      |> Option.defaultValue "/1-minute-of-silence.mp3"

    return
      [ _audio
          [ _id_ "audio-background-music"
            Attr.create
              "hx-on::before-cleanup-element"
              "fe.stopMusic(this)"
            Attr.create
              "hx-on::after-swap"
              "fe.startMusic(this)"
            Attr.create
              "hx-on::after-process-node"
              "fe.startMusic(this)"
            _loop_
            _src_ audioSrc ]
          [] ]
  }

let private stepToSpeakerImage animation step =
  let speakerStyle =
    "position: fixed; height: 60%; min-width: 5000px; object-fit: contain; bottom: 0dvh; align-self: center;"

  match step.show, step.speaker, step.emote with
  | false, _, _
  | _, None, _ ->
    _img
      [ _id_ $"img-speaker-{step.stepCount}"
        _style_ "display: none;" ]
    |> Handler.return'
  | true, Some char, emote ->
    handler {
      let! speaker =
        match step.runAs with
        | RunAsPublisher guid ->
          StoryAssets.findSpeakerAs guid char
        | RunAsWriter -> StoryAssets.findSpeaker char

      let src =
        match speaker, emote with
        | None, _ -> "/assets/speakers/shared/unknown.png"
        | Some s, None -> s.url
        | Some s, Some emote ->
          s.emotes
          |> List.tryFind (fun e -> e.emote = emote)
          |> Option.map (fun e -> e.url)
          |> Option.defaultValue s.url

      return
        _img
          [ _src_ src
            _id_ $"img-speaker-{step.stepCount}"
            _class_ animation
            _style_ speakerStyle ]
    }

let private makeSpeakerImage previous step =
  handler {
    // Don't fade out if it's the same image!
    let unchanged =
      previous.speaker = step.speaker
      && previous.emote = step.emote

    let fadeOutStyle =
      if unchanged then "hide" else "fade-out"

    let entryStyle =
      [ if not unchanged then yield "fade-in" else ()
        match step.animation with
        | Some a -> yield a
        | None -> () ]
      |> String.concat " "

    let! thisStepImg = stepToSpeakerImage entryStyle step

    let! prevStepImg =
      stepToSpeakerImage fadeOutStyle previous

    let speakerStyle =
      "position: fixed; height: 60%; min-width: 5000px; object-fit: contain; bottom: 0dvh; align-self: center;"

    return
      [ _div
          [ _id_ "img-speaker"; _style_ speakerStyle ]
          [ thisStepImg; prevStepImg ] ]
  }

let private makeChoiceBox guid step =
  handler {
    let! token = Handler.getCsrfToken ()
    let choiceMenu = makeChoiceMenu guid step token

    return
      _div
        [ _class_
            "is-flex is-flex-direction-column is-align-content-flex-end is-flex-grow-2"
          _id_ "choice-box" ]
        [ choiceMenu ]

  }

let private makeMessageBox step =
  let content = makeContentBox step

  _div
    [ _class_ "is-flex-grow-1" ]
    [ _div
        [ _class_ "container is-fluid" ]
        [ _div
            [ _class_ "box content has-text-centered mt-4"
              _style_
                "background-color: color-mix(in oklab, var(--bulma-box-background-color), transparent 10%)"
              _id_ "content-story" ]
            content ] ]

let makeContinueOverlay guid step =
  handler {
    let! token = Handler.getCsrfToken ()

    return
      if not step.finished && step.choices.Length = 0 then
        _div
          [ yield! playthroughHxAttrs guid token
            _id_ "continue-overlay"
            _style_
              "position: absolute; height: 100%; width: 100%; opacity: 0; z-index: 100;" ]
          []
      else
        _div
          [ _id_ "continue-overlay"
            _style_ "display: none;" ]
          []
  }

let makeBackgroundUnderlay step =
  handler {
    let! sceneUrl =
      match step.scene with
      | Some s ->
        let nameAndTag = s.Split(" ", 2)

        match nameAndTag with
        | [| name; tag |] ->
          (match step.runAs with
           | RunAsWriter -> StoryAssets.findScene name
           | RunAsPublisher guid ->
             StoryAssets.findSceneAs guid name)
          |> Handler.map (
            Option.bind (fun s ->
              s.tags |> List.tryFind (fun t -> t.tag = tag))
          )
          |> Handler.map (Option.map (fun t -> t.url))
        | [| name |] ->
          (match step.runAs with
           | RunAsWriter -> StoryAssets.findScene name
           | RunAsPublisher guid ->
             StoryAssets.findSceneAs guid name)
          |> Handler.map (Option.map (fun s -> s.url))
        | _ -> Handler.return' None
      | None -> Handler.return' None

    let backgroundStyle =
      match sceneUrl with
      | Some bg ->
        _style_ (
          "z-index: -1; height: 100dvh; width: 100vw; background-image: url('"
          + bg
          + "'); background-size: cover; position: fixed;"
        )
      | None ->
        _style_
          "position: fixed; z-index: -1; height: 100dvh; width: 100vw;"

    return
      _div
        [ _id_ "background-underlay"
          _class_ "has-background-light"
          backgroundStyle ]
        []
  }

let private closeButton step =
  let href =
      match step.runAs with
      | RunAsPublisher _ -> "/"
      | RunAsWriter -> $"/script/{step.scriptId.ToString()}"
  B.delete
    [ _class_ "m-2 is-medium"
      Hx.swapOuterHtml
      Hx.select "#page"
      Hx.targetCss "#page"
      Hx.get href
      _style_
        "position: absolute; top: 0; right: 0; z-index: 200;" ]

let currentView template guid steps =
  handler {
    let! audio = makeAudio steps

    let! speakerImage =
      makeSpeakerImage steps.previousStep steps.currentStep

    let! choiceBox = makeChoiceBox guid steps.currentStep
    let messageBox = makeMessageBox steps.currentStep

    let! continueOverlay =
      makeContinueOverlay guid steps.currentStep

    let! backgroundUnderlay =
      makeBackgroundUnderlay steps.currentStep

    return
      [ yield
          _section
            [ _class_ "hero"
              _style_ "height: 100dvh;"
              _id_ "hero-story"
              Hx.targetCss "#hero-story"
              // Attr.create "hx-on::load" "fe.requestFullscreenPlaythrough()"
              HxMorph.morphOuterHtml ]
            [ yield! audio
              yield! speakerImage
              closeButton steps.currentStep
              continueOverlay
              backgroundUnderlay
              messageBox
              choiceBox ] ]
      |> template
  }


let stepGet viewContext =
  handler {
    let! requestData = Handler.fromCtx Request.getRoute
    let id = requestData.GetGuid "guid"

    let! playthrough = load id

    let! html =
      currentView
        (viewContext.skeletalTemplate
          playthrough.steps.currentStep.gameTitle)
        id
        playthrough.steps

    return! HxFragment "hero-story" html
  }
  |> Handler.flatten
  |> get "/playthrough/{guid}"

let stepPost viewContext =
  handler {
    let! requestData = Handler.fromCtx Request.getRoute
    let id = requestData.GetGuid "guid"

    let! index =
      Handler.formDataOrFail
        (Response.withStatusCode 403 >> Response.ofEmpty)
        (fun buttonData ->
          buttonData.TryGetInt32 "choice" |> Some)

    let! step = cont id index

    let! html =
      currentView
        (viewContext.skeletalTemplate
          step.currentStep.gameTitle)
        id
        step

    return! HxFragment "hero-story" html
  }
  |> Handler.flatten
  |> post "playthrough/{guid:guid}"

let startPost viewContext =
  handler {
    let! scriptId =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun requestData -> requestData.TryGetGuid "script")

    let! script =
      Script.load scriptId
      |> Handler.ofOption (
        Response.withStatusCode 404 >> Response.ofEmpty
      )

    let! id = start script RunAsWriter

    let! playthrough = load id

    let! html =
      currentView
        (viewContext.skeletalTemplate
          playthrough.steps.currentStep.gameTitle)
        id
        playthrough.steps

    return
      Response.withHxPushUrl $"/playthrough/{id}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> post "/playthrough/start"

let startPublishedGet viewContext =
  handler {
    let! route = Handler.fromCtx Request.getRoute
    let slug = route.GetString "slug"

    let! script =
      DocStore.singleShared<Script, _> (fun q ->
        q .Where(fun s -> s.id = Slug.toGuid slug)
          .Where(fun s -> s.AnyTenant()))
      |> Handler.ofOption (
        Response.withStatusCode 404 >> Response.ofEmpty
      )

    let! id =
      start
        script
        (script.writerId
         |> System.Guid.Parse
         |> RunAsPublisher)

    printfn "Started playthrough"

    let! playthrough = load id

    printfn "Creating view"

    let! html =
      currentView
        (viewContext.skeletalTemplate
          playthrough.steps.currentStep.gameTitle)
        id
        playthrough.steps

    printfn "Responding"

    return
      Response.withHxPushUrl $"/playthrough/{id}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/published/{slug}"

let startExampleGet viewContext =
  handler {
    let! route = Handler.fromCtx Request.getRoute
    let filename = route.GetString "filename"
    let! script = getExampleScript $"{filename}.ink"

    let! id =
      start
        script
        (script.writerId
         |> System.Guid.Parse
         |> RunAsPublisher)

    printfn "Started playthrough"

    let! playthrough = load id

    printfn "Creating view"

    let! html =
      currentView
        (viewContext.skeletalTemplate
          playthrough.steps.currentStep.gameTitle)
        id
        playthrough.steps

    printfn "Responding"

    return
      Response.withHxPushUrl $"/playthrough/{id}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/examples/{filename}"

module Service =
  let endpoints viewContext =
    [ stepGet viewContext
      stepPost viewContext
      startPost viewContext
      startPublishedGet viewContext
      startExampleGet viewContext ]

  let addService: AddService =
    fun _ sc ->
      sc.ConfigureMarten(fun (storeOpts: StoreOptions) ->
        storeOpts.Schema
          .For<PlaythroughDocument>()
          .MultiTenanted()
        |> ignore)
